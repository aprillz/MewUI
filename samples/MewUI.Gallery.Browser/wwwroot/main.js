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

// Floor for the IME field, so a caret near the right edge still has a box a pre-edit fits in.
const MIN_TEXT_INPUT_WIDTH_PX = 200;
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

const { getAssemblyExports, getConfig, runMain, setModuleImports } = await dotnet.create();
// Writing needs a user gesture, which a copy or cut always is, and nothing waits on the result.
setModuleImports('main.js', {
    writeClipboard: text => { navigator.clipboard?.writeText(text).catch(() => {}); },
    // The browser hangs the IME candidate list off the focused field, so the field has to sit on
    // the caret. Left where it starts, the candidates appear in the top left corner of the page.
    moveTextInput: (x, y, height) => {
        const rect = canvas.getBoundingClientRect();
        textInput.style.left = `${rect.left + x}px`;
        textInput.style.top = `${rect.top + y}px`;
        textInput.style.height = `${Math.max(1, height)}px`;

        // The browser lays the pre-edit out inside this field and reports those bounds to the IME.
        // A field too narrow for the pre-edit scrolls it instead, which walks the reported start
        // leftward as the text grows and drags the candidate window along with it, so the field is
        // given the room the text on screen has.
        textInput.style.width = `${Math.max(MIN_TEXT_INPUT_WIDTH_PX, rect.width - x)}px`;
    },
});
const config = getConfig();
const exports = await getAssemblyExports(config.mainAssemblyName);
const app = exports.Aprillz.MewUI.Gallery.BrowserExports;

// ThemeVariant.System resolves through the host, so the page's colour scheme has to be in place
// before the first window is created, and a later change has to re-resolve it.
const darkScheme = window.matchMedia('(prefers-color-scheme: dark)');
app.SetSystemDarkMode(darkScheme.matches);
darkScheme.addEventListener('change', event => { wake(); app.SetSystemDarkMode(event.matches); });

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
            point.x - gesture.lastX, point.y - gesture.lastY, modifiersOf(event), event.timeStamp);
        gesture.lastX = point.x;
        gesture.lastY = point.y;
        return;
    }

    if (gesture !== null) {
        const travelX = point.x - gesture.startX;
        const travelY = point.y - gesture.startY;
        const travel = Math.hypot(travelX, travelY);
        if (travel > TOUCH_PAN_THRESHOLD_PX) {
            // Nothing took the press, so abandon it rather than leaving the control under the finger
            // pressed, and spend the rest of the gesture scrolling. Only the threshold itself is
            // given up: anchoring at the crossing point instead would drop this whole move, which
            // is what made a gesture travel less than the finger did.
            const consumed = TOUCH_PAN_THRESHOLD_PX / travel;
            gesture.panning = true;
            gesture.lastX = gesture.startX + travelX * consumed;
            gesture.lastY = gesture.startY + travelY * consumed;
            app.PointerCancel();
            app.PointerPan(gesture.startX, gesture.startY, event.screenX, event.screenY,
                point.x - gesture.lastX, point.y - gesture.lastY, modifiersOf(event), event.timeStamp);
            gesture.lastX = point.x;
            gesture.lastY = point.y;
            return;
        }
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
        // A finger let go mid-scroll leaves the content moving; the app reads the speed it was
        // tracking and coasts from there.
        app.PointerPanRelease(event.timeStamp);
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
// Focusing and blurring raise their events while this is still running, and the handlers that
// deliver text call back into here, so one pass has to finish before another starts.
let syncingTextInput = false;

function syncTextInputFocus() {
    if (syncingTextInput) {
        return;
    }

    syncingTextInput = true;
    try {
        const wanted = app.WantsTextInput();
        if (wanted && document.activeElement !== textInput) {
            textInput.focus({ preventScroll: true });
        } else if (!wanted && !composing && document.activeElement === textInput) {
            textInput.blur();
        }

        // The caret moves with every keystroke and every click inside the text, and the candidate
        // list is placed when composition starts, so the field has to already be there.
        if (wanted) {
            app.SyncTextCaret();
        }
    } finally {
        syncingTextInput = false;
    }
}

let composing = false;

// A soft keyboard reports 229 or no code at all, which is how an edit intent is told apart from a
// hardware key that already delivered itself.
let softKeyboardKey = false;

// Set by a held-back paste shortcut, so the replay carries the modifier the user actually pressed.
// A paste from the operating system menu leaves it null and falls back to the primary modifier.
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
    app.KeyDown('KeyV', 86, modifiers, false);
    app.KeyUp('KeyV', 86, modifiers);
});

window.addEventListener('keydown', event => {
    wake();
    softKeyboardKey = !event.code || event.code === 'Unidentified' || event.keyCode === 229;
    if (composing) {
        return;
    }

    // A browser reveals the clipboard only inside the paste event, which arrives after this keydown.
    // Holding the key back and replaying it from there lets the paste command read real text.
    if (event.code === 'KeyV' && (event.ctrlKey || event.metaKey) && !event.altKey) {
        pendingPasteModifiers = modifiersOf(event);
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

// The pre-edit is routed rather than only its result, so a composing control shows the text being
// built. Ending the composition commits what it carries, which is why no text input follows it.
textInput.addEventListener('compositionstart', () => { wake(); composing = true; app.CompositionStart(); });
textInput.addEventListener('compositionupdate', event => { wake(); app.CompositionUpdate(event.data ?? ''); });
textInput.addEventListener('compositionend', event => {
    wake();
    composing = false;
    app.CompositionEnd(event.data ?? '');
    textInput.value = '';
    syncTextInputFocus();
});

// A soft keyboard reports edits by intent rather than by key: it sends no usable key code, so the
// editing ones are turned back into the key the control expects. A hardware key already delivered
// its own keydown, and re-sending it here would apply the edit twice.
const EDIT_INTENT_KEYS = {
    deleteContentBackward: 'Backspace',
    deleteContentForward: 'Delete',
    deleteWordBackward: 'Backspace',
    insertLineBreak: 'Enter',
    insertParagraph: 'Enter',
};

textInput.addEventListener('beforeinput', event => {
    if (event.isComposing || !softKeyboardKey) {
        return;
    }

    const key = EDIT_INTENT_KEYS[event.inputType];
    if (key === undefined) {
        return;
    }

    wake();
    app.KeyDown(key, 0, 0, false);
    app.KeyUp(key, 0, 0);
    event.preventDefault();
});

textInput.addEventListener('input', event => {
    wake();
    if (composing || event.isComposing) {
        return;
    }

    if (event.inputType === 'insertText' && event.data) {
        app.TextInput(event.data);
        // keydown syncs before the text arrives, so without this the field trails the caret by a
        // character and the next composition opens its candidates there.
        syncTextInputFocus();
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

// The timestamp is the one the frame will be presented at. Anything animating off a clock read
// inside the frame instead moves by however long the frame took to get there, which is what makes
// a smooth curve step.
function frame(frameTimeMs) {
    frameScheduled = false;
    try {
        const dpr = syncCanvasSize();
        const drew = app.RenderFrame(
            canvas.clientWidth,
            canvas.clientHeight,
            dpr,
            canvas.width,
            canvas.height,
            frameTimeMs ?? performance.now());

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
