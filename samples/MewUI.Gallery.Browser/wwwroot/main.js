import { dotnet } from './_framework/dotnet.js'

const canvas = document.getElementById('canvas');
const status = document.getElementById('status');
const textInput = document.getElementById('textinput');
let pixelConfirmed = false;
let frameErrorCount = 0;
const MAX_LOGGED_FRAME_ERRORS = 5;

// Resizing the backing store clears the drawing buffer, and the context is created without
// preserveDrawingBuffer, so the resize has to happen in the frame that redraws it. Doing this
// from the resize event instead lets the browser composite a cleared buffer, which flickers.
function syncCanvasSize() {
    const dpr = window.devicePixelRatio || 1;
    const width = Math.max(1, Math.round(canvas.clientWidth * dpr));
    const height = Math.max(1, Math.round(canvas.clientHeight * dpr));
    if (canvas.width !== width || canvas.height !== height) {
        canvas.width = width;
        canvas.height = height;
        console.log(`[resize] css=${canvas.clientWidth}x${canvas.clientHeight} buffer=${width}x${height} dpr=${dpr}`);
    }

    return dpr;
}

syncCanvasSize();

const { getAssemblyExports, getConfig, runMain } = await dotnet.create();
const config = getConfig();
const exports = await getAssemblyExports(config.mainAssemblyName);
const app = exports.Aprillz.MewUI.Gallery.BrowserExports;
const runPromise = runMain(config.mainAssemblyName, []);

const MODIFIER_CONTROL = 1;
const MODIFIER_SHIFT = 2;
const MODIFIER_ALT = 4;
const MODIFIER_META = 8;

function modifiersOf(event) {
    return (event.ctrlKey ? MODIFIER_CONTROL : 0)
        | (event.shiftKey ? MODIFIER_SHIFT : 0)
        | (event.altKey ? MODIFIER_ALT : 0)
        | (event.metaKey ? MODIFIER_META : 0);
}

// Client coordinates are relative to the canvas box, so CSS scaling and page scroll stay correct;
// offsetX/offsetY would be wrong once the canvas is transformed.
function clientPoint(event) {
    const rect = canvas.getBoundingClientRect();
    return { x: event.clientX - rect.left, y: event.clientY - rect.top };
}

canvas.addEventListener('pointermove', event => {
    wake();
    const point = clientPoint(event);
    app.PointerMove(point.x, point.y, event.screenX, event.screenY, event.buttons, modifiersOf(event));
});

canvas.addEventListener('pointerdown', event => {
    wake();
    const point = clientPoint(event);
    canvas.setPointerCapture(event.pointerId);
    focusInput();
    const captured = app.PointerButton(point.x, point.y, event.screenX, event.screenY,
        event.button, event.buttons, true, event.detail || 1, modifiersOf(event));
    if (!captured) {
        canvas.releasePointerCapture(event.pointerId);
    }
    event.preventDefault();
});

canvas.addEventListener('pointerup', event => {
    wake();
    const point = clientPoint(event);
    const captured = app.PointerButton(point.x, point.y, event.screenX, event.screenY,
        event.button, event.buttons, false, event.detail || 1, modifiersOf(event));
    if (!captured && canvas.hasPointerCapture(event.pointerId)) {
        canvas.releasePointerCapture(event.pointerId);
    }
});

canvas.addEventListener('pointercancel', () => { wake(); app.PointerCancel(); });
canvas.addEventListener('pointerleave', () => { wake(); app.PointerLeave(); });

// MewUI counts wheel movement in notches with +Y up and +X left, while the DOM reports pixels,
// lines or pages with +Y down and +X right.
const WHEEL_PIXELS_PER_NOTCH = 100;
const WHEEL_LINES_PER_NOTCH = 3;

function wheelNotches(delta, deltaMode) {
    if (deltaMode === 1) {
        return -delta / WHEEL_LINES_PER_NOTCH;
    }

    if (deltaMode === 2) {
        return -delta;
    }

    return -delta / WHEEL_PIXELS_PER_NOTCH;
}

canvas.addEventListener('wheel', event => {
    wake();
    const point = clientPoint(event);
    app.PointerWheel(point.x, point.y, event.screenX, event.screenY,
        wheelNotches(event.deltaX, event.deltaMode),
        wheelNotches(event.deltaY, event.deltaMode),
        event.buttons, modifiersOf(event));
    event.preventDefault();
}, { passive: false });

canvas.addEventListener('contextmenu', event => event.preventDefault());

// Keyboard and text input run through a visually hidden input so the browser keeps producing
// composition and text events; the canvas itself never receives text.
function focusInput() {
    if (document.activeElement !== textInput) {
        textInput.focus({ preventScroll: true });
    }
}

let composing = false;

textInput.addEventListener('keydown', event => {
    wake();
    if (composing) {
        return;
    }

    const handled = app.KeyDown(event.code, event.keyCode || 0, modifiersOf(event), event.repeat);
    // Tab and browser shortcuts would otherwise move focus out of the canvas.
    if (handled || event.code === 'Tab' || event.code === 'Space' || event.code.startsWith('Arrow')) {
        event.preventDefault();
    }
});

textInput.addEventListener('keyup', event => {
    wake();
    if (!composing) {
        app.KeyUp(event.code, event.keyCode || 0, modifiersOf(event));
    }
});

textInput.addEventListener('compositionstart', () => { composing = true; });
textInput.addEventListener('compositionend', event => {
    wake();
    composing = false;
    if (event.data) {
        app.TextInput(event.data);
    }
    textInput.value = '';
});

textInput.addEventListener('input', event => {
    wake();
    if (composing || event.isComposing) {
        return;
    }

    if (event.inputType === 'insertText' && event.data) {
        app.TextInput(event.data);
    }

    textInput.value = '';
});

textInput.addEventListener('focus', () => { wake(); app.FocusChanged(true); });
textInput.addEventListener('blur', () => { wake(); app.FocusChanged(false); });

window.addEventListener('blur', () => { wake(); app.FocusChanged(false); });

focusInput();

// The gallery binds its images and icon dictionary to values a host fills. There is no disk here,
// so they are fetched alongside the first frames and arrive through the same late-binding path the
// desktop app uses when it downloads them.
async function loadResources() {
    const names = app.ResourceFileNames();
    for (const name of names) {
        try {
            const response = await fetch(`./Resources/${name}`);
            if (!response.ok) {
                console.warn(`MewUI Gallery resource ${name} failed: ${response.status}`);
                continue;
            }

            app.ApplyResource(name, new Uint8Array(await response.arrayBuffer()));
            wake();
        } catch (error) {
            console.warn(`MewUI Gallery resource ${name} failed.`, error);
        }
    }
}

loadResources();

// The loop idles instead of repainting at the display's refresh rate: a frame runs only while the
// app reports work, and anything that can change the screen wakes it again.
let frameScheduled = false;
let idleFrames = 0;
let wakeTimer = 0;
const IDLE_FRAMES_BEFORE_SLEEP = 3;

function wake() {
    idleFrames = 0;
    if (wakeTimer !== 0) {
        clearTimeout(wakeTimer);
        wakeTimer = 0;
    }
    if (!frameScheduled) {
        frameScheduled = true;
        requestAnimationFrame(frame);
    }
}

// A sleeping loop still owes scheduled work: the app reports when its next timer is due, the same
// value desktop hosts hand to their OS wait, and the timeout brings the loop back for it.
function sleepUntilNextTimer() {
    const delay = app.NextWakeDelayMs();
    if (delay < 0 || wakeTimer !== 0) {
        return;
    }

    wakeTimer = setTimeout(() => { wakeTimer = 0; wake(); }, delay);
}

function frame() {
    frameScheduled = false;
    try {
        const dpr = syncCanvasSize();
        const drew = app.RenderFrame(
            canvas.clientWidth,
            canvas.clientHeight,
            dpr,
            canvas.width,
            canvas.height);

        // A few quiet frames in a row before sleeping, so a render that only queues more work
        // (layout settling, a late resource) still gets its follow-up frame.
        idleFrames = drew ? 0 : idleFrames + 1;

        if (!pixelConfirmed) {
            const gl = canvas.getContext('webgl2');
            if (gl) {
                const pixel = new Uint8Array(4);
                gl.readPixels(
                    Math.max(0, Math.floor(canvas.width / 2)),
                    Math.max(0, Math.floor(canvas.height / 2)),
                    1,
                    1,
                    gl.RGBA,
                    gl.UNSIGNED_BYTE,
                    pixel);
                if (pixel[0] !== 0 || pixel[1] !== 0 || pixel[2] !== 0) {
                    pixelConfirmed = true;
                    status.textContent = 'MewUI Gallery First Boot: rendered';
                    console.log('MewUI Gallery first pixel confirmed.', Array.from(pixel));
                }
            }
        }
    } catch (error) {
        // Keep the loop alive: stopping here freezes the canvas, and every later symptom looks
        // like resize or input being broken rather than the one frame that actually failed.
        frameErrorCount++;
        status.textContent = `MewUI Gallery frame failed (${frameErrorCount}): ${error}`;
        if (frameErrorCount <= MAX_LOGGED_FRAME_ERRORS) {
            console.error('MewUI Gallery frame failed.', error);
        }
        idleFrames = 0;
    }

    if (idleFrames < IDLE_FRAMES_BEFORE_SLEEP) {
        frameScheduled = true;
        requestAnimationFrame(frame);
    } else {
        sleepUntilNextTimer();
    }
}

wake();
window.addEventListener('resize', wake);
runPromise.catch(error => {
    status.textContent = 'MewUI Gallery failed - see console';
    console.error('MewUI Gallery runtime failed.', error);
});
