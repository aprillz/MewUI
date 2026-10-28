// The page owns the canvas, the frame loop and the DOM events; the app draws into the canvas and
// handles what the events mean. Every call into the app goes through BrowserExports.cs.

const canvas = document.getElementById('canvas');
const textInput = document.getElementById('textinput');
const loader = document.getElementById('loader');

function reportFatal(error) {
    const detail = error && (error.message || error.reason && error.reason.message || error.reason) || error;
    loader.hidden = false;
    loader.classList.add('failed');
    loader.textContent = `Startup failed: ${String(detail).slice(0, 400)}`;
}

window.addEventListener('error', event => reportFatal(event.error ?? event.message));
window.addEventListener('unhandledrejection', event => reportFatal(event.reason));

// dotnet.js is the one runtime asset the SDK leaves unfingerprinted, and it names every other asset.
// Carrying this file's own query string over keeps a cached dotnet.js from pointing at an old deploy.
const VERSION_QUERY = new URL(import.meta.url).search;
const { dotnet } = await import(`./_framework/dotnet.js${VERSION_QUERY}`);

// The platform ships the page side of text input and IME composition; the page hands it its field.
const { createTextInputBridge } = await import(`./mewui-text-input.js${VERSION_QUERY}`);

const { getAssemblyExports, getConfig, runMain, setModuleImports } = await dotnet.create();

// The two calls the app makes back into the page. Writing the clipboard needs a user gesture, which
// a copy or cut always is.
setModuleImports('main.js', {
    writeClipboard: text => { navigator.clipboard?.writeText(text).catch(() => {}); },
    moveTextInput: (x, y, height) => textInputBridge.place(x, y, height),
});

const config = getConfig();
const exports = await getAssemblyExports(config.mainAssemblyName);
const app = exports.MewUIBrowserApp.BrowserExports;
const textInputBridge = createTextInputBridge({ app, canvas, field: textInput, wake });

// ThemeVariant.System resolves through the page, so the scheme is reported before the window exists.
const darkScheme = window.matchMedia('(prefers-color-scheme: dark)');
app.SetSystemDarkMode(darkScheme.matches);
darkScheme.addEventListener('change', event => { wake(); app.SetSystemDarkMode(event.matches); });

runMain(config.mainAssemblyName, []).catch(reportFatal);

// --- Canvas size ---

// Resizing the backing store clears it, so the resize happens inside the frame that redraws it.
// The box is measured rather than read from clientWidth, which rounds to whole CSS pixels and at a
// fractional display scale sizes the buffer a pixel away from the box it is composited into.
let canvasCssWidth = 0;
let canvasCssHeight = 0;

function syncCanvasSize() {
    const dpr = window.devicePixelRatio || 1;
    const rect = canvas.getBoundingClientRect();
    canvasCssWidth = rect.width;
    canvasCssHeight = rect.height;
    const width = Math.max(1, Math.round(rect.width * dpr));
    const height = Math.max(1, Math.round(rect.height * dpr));
    if (canvas.width !== width || canvas.height !== height) {
        canvas.width = width;
        canvas.height = height;
    }

    return dpr;
}

// --- Pointer ---

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

// Relative to the canvas box, so CSS scaling and page scroll stay correct.
function clientPoint(event) {
    const rect = canvas.getBoundingClientRect();
    return { x: event.clientX - rect.left, y: event.clientY - rect.top };
}

// Touch and pen contacts go to the app as they are, and the app reads taps, scrolls and holds from
// them. The mouse keeps its own path.
const POINTER_TOUCH = 1;
const POINTER_PEN = 2;

function contactTypeOf(event) {
    return event.pointerType === 'touch' ? POINTER_TOUCH : POINTER_PEN;
}

// How long ago the browser received the event, so the app can place it on its own clock.
function ageOf(event) {
    return Math.max(0, performance.now() - event.timeStamp);
}

const activeContacts = new Set();

canvas.addEventListener('pointermove', event => {
    wake();
    const point = clientPoint(event);
    if (event.pointerType === 'mouse') {
        app.MouseMove(point.x, point.y, event.screenX, event.screenY, event.buttons, modifiersOf(event));
        return;
    }

    app.PointerMove(event.pointerId, contactTypeOf(event), point.x, point.y, event.screenX, event.screenY,
        ageOf(event), modifiersOf(event));
});

canvas.addEventListener('pointerdown', event => {
    wake();
    const point = clientPoint(event);
    if (event.pointerType === 'mouse') {
        canvas.setPointerCapture(event.pointerId);
        const captured = app.MouseButton(point.x, point.y, event.screenX, event.screenY,
            event.button, event.buttons, true, event.timeStamp, modifiersOf(event));
        if (!captured) {
            canvas.releasePointerCapture(event.pointerId);
        }
    } else {
        // The capture makes the release land on the canvas even when the finger ends past its edge.
        activeContacts.add(event.pointerId);
        canvas.setPointerCapture(event.pointerId);
        app.PointerDown(event.pointerId, contactTypeOf(event), point.x, point.y, event.screenX, event.screenY,
            ageOf(event), modifiersOf(event));
    }

    // The press decides what has focus, and running inside the gesture lets a phone raise its keyboard.
    textInputBridge.sync();
    event.preventDefault();
});

canvas.addEventListener('pointerup', event => {
    wake();
    const point = clientPoint(event);
    if (event.pointerType === 'mouse') {
        const captured = app.MouseButton(point.x, point.y, event.screenX, event.screenY,
            event.button, event.buttons, false, event.timeStamp, modifiersOf(event));
        if (!captured && canvas.hasPointerCapture(event.pointerId)) {
            canvas.releasePointerCapture(event.pointerId);
        }
        return;
    }

    activeContacts.delete(event.pointerId);
    app.PointerUp(event.pointerId, contactTypeOf(event), point.x, point.y, event.screenX, event.screenY,
        ageOf(event), modifiersOf(event));
    if (canvas.hasPointerCapture(event.pointerId)) {
        canvas.releasePointerCapture(event.pointerId);
    }
});

canvas.addEventListener('pointercancel', event => {
    wake();
    if (event.pointerType === 'mouse') {
        app.MouseCancel();
        return;
    }

    activeContacts.delete(event.pointerId);
    app.PointerCancel(event.pointerId);
});

// A contact the canvas never sees end would otherwise stay down and turn every later touch into a
// second finger.
function forgetContact(pointerId) {
    if (activeContacts.delete(pointerId)) {
        wake();
        app.PointerCancel(pointerId);
    }
}

window.addEventListener('pointerup', event => forgetContact(event.pointerId));
window.addEventListener('pointercancel', event => forgetContact(event.pointerId));
document.addEventListener('visibilitychange', () => {
    if (document.hidden) {
        for (const pointerId of [...activeContacts]) {
            forgetContact(pointerId);
        }
    }
});
canvas.addEventListener('pointerleave', () => { wake(); app.PointerLeave(); });

// The app counts wheel movement in notches with +Y up and +X left; the DOM reports pixels, lines or
// pages with +Y down and +X right.
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

// --- Keyboard and clipboard ---

// Set by a held-back paste shortcut, so the replay carries the modifier the user pressed.
let pendingPasteModifiers = null;
const APPLE_PLATFORM = /Mac|iPhone|iPad|iPod/i.test(navigator.platform || navigator.userAgent || '');

textInput.addEventListener('paste', event => {
    const text = event.clipboardData?.getData('text/plain') ?? '';
    const modifiers = pendingPasteModifiers ?? (APPLE_PLATFORM ? MODIFIER_META : MODIFIER_CONTROL);
    pendingPasteModifiers = null;
    event.preventDefault();
    if (text.length === 0) {
        return;
    }

    wake();
    app.SetClipboardText(text);
    app.KeyDown('KeyV', 86, modifiers, false, 'v');
    app.KeyUp('KeyV', 86, modifiers, 'v');
});

window.addEventListener('keydown', event => {
    wake();
    if (textInputBridge.claimsKey(event)) {
        return;
    }

    // The clipboard is readable only inside the paste event, which arrives after this keydown, so
    // the shortcut is held back and replayed from there.
    if (event.code === 'KeyV' && (event.ctrlKey || event.metaKey) && !event.altKey) {
        pendingPasteModifiers = modifiersOf(event);
        return;
    }

    const handled = app.KeyDown(event.code, event.keyCode || 0, modifiersOf(event), event.repeat, event.key);
    // Tab or an arrow would otherwise move focus out of the canvas or scroll the page.
    if (handled || event.code === 'Tab' || event.code.startsWith('Arrow')) {
        event.preventDefault();
    }

    textInputBridge.sync();
});

window.addEventListener('keyup', event => {
    wake();
    if (!textInputBridge.composing && event.keyCode !== 229 && event.key !== 'Process') {
        app.KeyUp(event.code, event.keyCode || 0, modifiersOf(event), event.key);
    }
});

// Window activation follows the page, not the hidden field, which comes and goes with text focus.
window.addEventListener('focus', () => { wake(); app.FocusChanged(true); });
window.addEventListener('blur', () => {
    wake();
    // A composition left mid-flight gets no compositionend of its own.
    textInputBridge.endComposition();
    app.FocusChanged(false);
});

app.FocusChanged(document.hasFocus());

// --- Frame loop ---

// A frame runs only while the app reports work, and every event wakes the loop again.
const IDLE_FRAMES_BEFORE_SLEEP = 3;
let frameScheduled = false;
let idleFrames = 0;
let wakeTimer = 0;

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

// A sleeping loop still owes the app's timers: it reports when the next one is due.
function sleepUntilNextTimer() {
    const delay = app.NextWakeDelayMs();
    if (delay < 0 || wakeTimer !== 0) {
        return;
    }

    wakeTimer = setTimeout(() => { wakeTimer = 0; wake(); }, delay);
}

// The timestamp is the one the frame is presented at; animating off a clock read inside the frame
// instead moves by however long the frame took to get there.
function frame(frameTimeMs) {
    frameScheduled = false;
    try {
        const dpr = syncCanvasSize();
        const drew = app.RenderFrame(canvasCssWidth, canvasCssHeight, dpr, canvas.width, canvas.height,
            frameTimeMs === undefined ? 0 : Math.max(0, performance.now() - frameTimeMs));

        // A few quiet frames before sleeping, so a frame that only queues more work gets its follow-up.
        idleFrames = drew ? 0 : idleFrames + 1;
        if (drew && !loader.hidden && !loader.classList.contains('failed')) {
            loader.hidden = true;
        }
    } catch (error) {
        // The loop keeps running: stopping here freezes the canvas, and every later symptom then looks
        // like resize or input being broken.
        console.error('Frame failed.', error);
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
