// dotnet.js is the one asset the SDK leaves unfingerprinted, and it carries the hashed names of
// every other asset. A host that caches it hands an old manifest to a new deploy, so the runtime
// requests files that no longer exist. Pages offers no cache headers, so this file's own version
// query is carried over to pin both halves of the loader to the same deploy.
const { dotnet } = await import(`./_framework/dotnet.js${new URL(import.meta.url).search}`);

const canvas = document.getElementById('canvas');
const status = document.getElementById('status');
const textInput = document.getElementById('textinput');
let pixelConfirmed = false;
let frameErrorCount = 0;
const MAX_LOGGED_FRAME_ERRORS = 5;
let frameScheduled = false;
let idleFrames = 0;
let wakeTimer = 0;
const IDLE_FRAMES_BEFORE_SLEEP = 3;

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

// The canvas sets touch-action: none to keep the browser from zooming or panning it away, which
// also means a finger drag over empty content would do nothing. Such a drag is turned into a
// scroll instead. CaptureConsumesDrag decides who owns the gesture: a button captures the mouse
// only to see whether the release counts as a click, so the scroll may take it over, while a
// slider, scroll bar or splitter captures to consume the movement and keeps it.
const TOUCH_PAN_THRESHOLD_PX = 8;

// Selection waits for the release on touch, so the press has to say which device sent it.
function pointerTypeOf(event) {
    if (event.pointerType === 'touch') {
        return 1;
    }

    if (event.pointerType === 'pen') {
        return 2;
    }

    return 0;
}

let touchGesture = null;
const activePointers = new Set();

function endTouchGesture(pointerId) {
    if (touchGesture !== null && (pointerId === undefined || touchGesture.pointerId === pointerId)) {
        touchGesture = null;
    }
}

// A gesture whose pointer is no longer down was stranded by a release the page never saw. Pointer
// ids keep climbing, so without this the leftover would keep failing the "one finger at a time"
// test and every later drag would be taken for a click.
function dropStrandedTouchGesture() {
    if (touchGesture !== null && !activePointers.has(touchGesture.pointerId)) {
        touchGesture = null;
    }
}

canvas.addEventListener('pointermove', event => {
    wake();
    const point = clientPoint(event);
    const gesture = touchGesture !== null && touchGesture.pointerId === event.pointerId ? touchGesture : null;

    if (gesture !== null && gesture.panning) {
        app.PointerPan(gesture.startX, gesture.startY, event.screenX, event.screenY,
            point.x - gesture.lastX, point.y - gesture.lastY, modifiersOf(event));
        gesture.lastX = point.x;
        gesture.lastY = point.y;
        return;
    }

    if (gesture !== null && Math.hypot(point.x - gesture.startX, point.y - gesture.startY) > TOUCH_PAN_THRESHOLD_PX) {
        // Nothing took the press, so abandon it rather than leaving the control under the finger
        // pressed, and spend the rest of the gesture scrolling.
        gesture.panning = true;
        gesture.lastX = point.x;
        gesture.lastY = point.y;
        app.PointerCancel();
        return;
    }

    app.PointerMove(point.x, point.y, event.screenX, event.screenY, event.buttons, modifiersOf(event));
    if (gesture !== null && app.CaptureConsumesDrag()) {
        endTouchGesture(event.pointerId);
    }
});

canvas.addEventListener('pointerdown', event => {
    wake();
    const point = clientPoint(event);
    activePointers.add(event.pointerId);
    endTouchGesture(event.pointerId);
    dropStrandedTouchGesture();
    canvas.setPointerCapture(event.pointerId);
    const captured = app.PointerButton(point.x, point.y, event.screenX, event.screenY,
        event.button, event.buttons, true, event.detail || 1, modifiersOf(event), pointerTypeOf(event));

    // The press decides what has focus, so the text field follows it rather than the other way
    // round. This still runs inside the gesture, which is what lets a phone raise its keyboard.
    syncTextInputFocus();

    // Only the first finger drives a scroll; a second one is left to the normal pointer path.
    const tracksTouch = event.pointerType === 'touch' && !app.CaptureConsumesDrag() && touchGesture === null;
    if (tracksTouch) {
        touchGesture = { pointerId: event.pointerId, startX: point.x, startY: point.y, lastX: point.x, lastY: point.y, panning: false };
    }

    // Keeping the capture is what makes the release land on the canvas even when the finger ends
    // up over the status overlay or past the edge of the window. Letting it go there would strand
    // the gesture, and every later drag would be taken for a click.
    if (!captured && !tracksTouch) {
        canvas.releasePointerCapture(event.pointerId);
    }

    event.preventDefault();
});

canvas.addEventListener('pointerup', event => {
    wake();
    const panned = touchGesture !== null && touchGesture.pointerId === event.pointerId && touchGesture.panning;
    endTouchGesture(event.pointerId);

    // The press was already cancelled when the scroll began, so releasing would report a click.
    if (panned) {
        if (canvas.hasPointerCapture(event.pointerId)) {
            canvas.releasePointerCapture(event.pointerId);
        }
        return;
    }

    const point = clientPoint(event);
    const captured = app.PointerButton(point.x, point.y, event.screenX, event.screenY,
        event.button, event.buttons, false, event.detail || 1, modifiersOf(event), pointerTypeOf(event));
    if (!captured && canvas.hasPointerCapture(event.pointerId)) {
        canvas.releasePointerCapture(event.pointerId);
    }
});

canvas.addEventListener('pointercancel', event => { wake(); endTouchGesture(event.pointerId); app.PointerCancel(); });

// Last resort for a release the canvas never sees, so one stranded gesture cannot disable panning
// for the rest of the session.
function forgetPointer(pointerId) {
    activePointers.delete(pointerId);
    endTouchGesture(pointerId);
}

window.addEventListener('pointerup', event => forgetPointer(event.pointerId));
window.addEventListener('pointercancel', event => forgetPointer(event.pointerId));

// Leaving the page can end a touch without any release reaching it at all.
document.addEventListener('visibilitychange', () => {
    if (document.hidden) {
        activePointers.clear();
        endTouchGesture();
    }
});
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

// Text and composition run through a visually hidden input, because the canvas itself never
// receives them. Focusing that input is what raises the on-screen keyboard on a phone, so it is
// held only while a text control has focus; keys are taken from the window so they arrive either
// way. Composition state has to settle before the focus moves, or the commit is lost.
function syncTextInputFocus() {
    const wanted = app.WantsTextInput();
    if (wanted && document.activeElement !== textInput) {
        textInput.focus({ preventScroll: true });
    } else if (!wanted && !composing && document.activeElement === textInput) {
        textInput.blur();
    }
}

let composing = false;

window.addEventListener('keydown', event => {
    wake();
    if (composing) {
        return;
    }

    const handled = app.KeyDown(event.code, event.keyCode || 0, modifiersOf(event), event.repeat);
    // Tab and browser shortcuts would otherwise move focus out of the canvas.
    if (handled || event.code === 'Tab' || event.code === 'Space' || event.code.startsWith('Arrow')) {
        event.preventDefault();
    }

    // A key can move focus onto or off a text control.
    syncTextInputFocus();
});

window.addEventListener('keyup', event => {
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

// Window activation is the page's own, not the hidden input's, which comes and goes with text focus.
window.addEventListener('focus', () => { wake(); app.FocusChanged(true); });
window.addEventListener('blur', () => { wake(); app.FocusChanged(false); });

app.FocusChanged(document.hasFocus());

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
