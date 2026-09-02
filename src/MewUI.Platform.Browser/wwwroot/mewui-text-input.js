// Text and composition for a canvas-rendered MewUI application.
//
// The canvas never receives text or composition events, so they run through a visually hidden
// input. That field is not a keystroke sink: it mirrors the text around the application's caret,
// because an IME reads and edits the field rather than the application. Korean hanja conversion,
// for one, deletes the run it is revising and reopens it as a composition, and with nothing in the
// field the browser drops the conversion without an event. What comes back is read as a change to
// the mirrored text, so typing, a soft keyboard's edits and an IME's own deletions all take one
// path instead of being translated from inputType, which differs between browsers and IMEs.

// Floor for the field, so a caret near the right edge still has a box a pre-edit fits in.
const MIN_FIELD_WIDTH_PX = 200;

// Keys that either move the caret or are taken by the IME to navigate its own composition.
const CARET_KEYS = new Set([
    'ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End', 'PageUp', 'PageDown',
]);

/**
 * Connects a hidden input to the application as its text and IME surface.
 *
 * app     - the exported application interface (WantsTextInput, GetTextInputState, ReplaceText,
 *           Composition*, SyncTextCaret, KeyDown, KeyUp).
 * canvas  - the canvas the application draws on, which the field is positioned against.
 * field   - the hidden input element.
 * wake    - called before anything that needs a frame drawn.
 */
export function createTextInputBridge({ app, canvas, field, wake }) {
    let composing = false;
    let syncing = false;
    let pendingCaretKey = null;
    let mirrorValue = '';
    let mirrorSelectionStart = 0;
    let mirrorSelectionEnd = 0;
    let measureContext = null;

    function measureWidth(text) {
        if (text.length === 0) {
            return 0;
        }

        if (measureContext === null) {
            measureContext = document.createElement('canvas').getContext('2d');
        }

        measureContext.font = window.getComputedStyle(field).font;
        return measureContext.measureText(text).width;
    }

    function readField() {
        return {
            value: field.value,
            selectionStart: field.selectionStart ?? 0,
            selectionEnd: field.selectionEnd ?? 0,
        };
    }

    function captureMirror() {
        const state = readField();
        mirrorValue = state.value;
        mirrorSelectionStart = state.selectionStart;
        mirrorSelectionEnd = state.selectionEnd;
    }

    // Pulls the application's text around the caret into the field. Never while composing: the
    // field belongs to the IME until it ends, and writing to it would cancel the pre-edit.
    function refillMirror() {
        if (composing) {
            return;
        }

        const state = app.GetTextInputState();
        let value = '';
        let selectionStart = 0;
        let selectionEnd = 0;
        if (state.length !== 0) {
            const firstSeparator = state.indexOf(':');
            const secondSeparator = state.indexOf(':', firstSeparator + 1);
            selectionStart = Number(state.substring(0, firstSeparator));
            selectionEnd = Number(state.substring(firstSeparator + 1, secondSeparator));
            value = state.substring(secondSeparator + 1);
        }

        if (field.value !== value) {
            field.value = value;
        }

        if (field.selectionStart !== selectionStart || field.selectionEnd !== selectionEnd) {
            field.setSelectionRange(selectionStart, selectionEnd);
        }

        captureMirror();
    }

    // Focusing the field is what raises the on-screen keyboard on a phone, so it is held only while
    // a text control has focus; keys are taken from the window so they arrive either way. Focusing
    // and blurring raise their events while this is still running, and the handlers that deliver
    // text call back into here, so one pass has to finish before another starts.
    function sync(refill = true) {
        if (syncing) {
            return;
        }

        syncing = true;
        try {
            const wanted = app.WantsTextInput();
            if (wanted && document.activeElement !== field) {
                field.focus({ preventScroll: true });
            } else if (!wanted && !composing && document.activeElement === field) {
                field.blur();
            }

            // The caret moves with every keystroke and every click inside the text, and the
            // candidate list is placed when composition starts, so the field has to already be
            // there. The mirror is filled first because the field is placed against what it holds.
            if (wanted && refill) {
                refillMirror();
                app.SyncTextCaret();
            } else if (wanted) {
                captureMirror();
                app.SyncTextCaret();
            } else if (!composing) {
                field.value = '';
                captureMirror();
            }
        } finally {
            syncing = false;
        }
    }

    // The browser hangs the IME candidate list off the focused field, so the field has to sit on
    // the caret. Left where it starts, the candidates appear in the top left corner of the page.
    function place(x, y, height) {
        const rect = canvas.getBoundingClientRect();

        // The field mirrors the text around the caret, so the caret inside it sits as far in as
        // that text is wide. The field is pulled left by exactly that much to put its caret back
        // over the application's, which is where the candidate list belongs.
        const lead = measureWidth(field.value.substring(0, field.selectionStart ?? 0));
        field.style.left = `${Math.max(0, rect.left + x - lead)}px`;
        field.style.top = `${rect.top + y}px`;
        field.style.height = `${Math.max(1, height)}px`;

        // The browser lays the pre-edit out inside this field and reports those bounds to the IME.
        // A field too narrow for the pre-edit scrolls it instead, which walks the reported start
        // leftward as the text grows and drags the candidate window along with it, so the field is
        // given the room the text on screen has, plus what the mirrored lead takes up.
        field.style.width = `${Math.max(MIN_FIELD_WIDTH_PX, rect.width - x + lead)}px`;
    }

    function commonPrefixLength(first, second) {
        const limit = Math.min(first.length, second.length);
        let index = 0;
        while (index < limit && first.charCodeAt(index) === second.charCodeAt(index)) {
            index++;
        }

        return index;
    }

    function commonSuffixLength(first, second) {
        const limit = Math.min(first.length, second.length);
        let index = 0;
        while (index < limit && first.charCodeAt(first.length - index - 1) === second.charCodeAt(second.length - index - 1)) {
            index++;
        }

        return index;
    }

    // Turns "the field held this, now it holds that" into a replacement around the caret. The
    // common head and tail are bounded by both selections so a repeated character next to the
    // caret is not mistaken for the one that was typed.
    function deduceInput(previous, current) {
        const prefix = Math.min(
            commonPrefixLength(previous.value, current.value),
            previous.selectionStart,
            current.selectionStart);
        const suffix = Math.min(
            commonSuffixLength(previous.value, current.value),
            previous.value.length - previous.selectionEnd,
            current.value.length - current.selectionEnd);
        const text = current.value.substring(prefix, current.value.length - suffix);
        const replacePrevious = previous.selectionStart === previous.selectionEnd
            ? previous.selectionStart - prefix
            : previous.selectionEnd - previous.selectionStart;
        return { text, replacePrevious, replaceNext: previous.value.length - suffix - previous.selectionEnd };
    }

    /**
     * True when the key belongs to the IME and must not reach the application. The caller still
     * routes every other key itself, because a key is more than text.
     */
    function claimsKey(event) {
        if (composing) {
            // A caret key can end the composition instead of being consumed by it, and the caret it
            // asks for has to reach the control. Which of the two happened is only known once the
            // IME answers with an update or an end, so the key waits until then.
            if (CARET_KEYS.has(event.code)) {
                pendingCaretKey = {
                    code: event.code,
                    keyCode: event.keyCode || 0,
                    modifiers: modifiersOf(event),
                };
            }

            return true;
        }

        // 229 is the IME consuming the key itself, which is how it navigates the candidate list it
        // opened. Forwarding it would take the key away from the IME and act on it twice.
        return event.keyCode === 229 || event.key === 'Process';
    }

    // Modifier bits as the platform host reads them.
    function modifiersOf(event) {
        return (event.ctrlKey ? 1 : 0)
            | (event.shiftKey ? 2 : 0)
            | (event.altKey ? 4 : 0)
            | (event.metaKey ? 8 : 0);
    }

    /** Closes a composition the page is leaving mid-flight, so no pre-edit is left on screen. */
    function endComposition() {
        if (!composing) {
            return;
        }

        composing = false;
        pendingCaretKey = null;
        app.CompositionEnd('');
    }

    // The pre-edit is routed rather than only its result, so a composing control shows the text
    // being built. Ending the composition commits what it carries, which is why no text input
    // follows it.
    field.addEventListener('compositionstart', () => {
        wake();
        composing = true;

        // The field keeps what it holds. An IME decides for itself how far back a conversion
        // reaches and asks the browser to delete that much before recomposing it; a field trimmed
        // to less than that has the deletion fall short and the text it re-inserts arrives twice.
        captureMirror();
        pendingCaretKey = null;
        app.CompositionStart();
    });

    field.addEventListener('compositionupdate', event => {
        wake();
        // The composition lives on, so the IME took the caret key for itself.
        pendingCaretKey = null;
        app.CompositionUpdate(event.data ?? '');
    });

    field.addEventListener('compositionend', event => {
        wake();
        composing = false;
        app.CompositionEnd(event.data ?? '');

        if (pendingCaretKey !== null) {
            const key = pendingCaretKey;
            pendingCaretKey = null;
            app.KeyDown(key.code, key.keyCode, key.modifiers, false);
            app.KeyUp(key.code, key.keyCode, key.modifiers);
            // The caret left the committed text, so there is nothing there to convert and the
            // mirror follows the control again.
            sync();
            return;
        }

        // The committed text is left in the field: a conversion that follows revises exactly it,
        // and refilling the mirror here would take away what the IME is about to act on.
        sync(false);
    });

    field.addEventListener('input', event => {
        wake();
        if (composing || event.isComposing) {
            return;
        }

        // The browser can write a finished composition into the field after it reported the end.
        // That text already reached the control through the composition, so only the mirror is
        // caught up.
        if (event.inputType === 'insertCompositionText') {
            captureMirror();
            return;
        }

        // What the browser did to the field is read as a change to the application's own text: the
        // field mirrors it, so a diff of value and selection says what to replace and with what.
        // This covers typing, a soft keyboard's edits, and the deletion an IME issues before it
        // reconverts a run.
        const current = readField();
        const previous = { value: mirrorValue, selectionStart: mirrorSelectionStart, selectionEnd: mirrorSelectionEnd };
        captureMirror();
        const change = deduceInput(previous, current);
        if (change.text.length === 0 && change.replacePrevious === 0 && change.replaceNext === 0) {
            return;
        }

        // Half of a surrogate pair means nothing on its own; the state is kept so the next event
        // delivers the whole character.
        if (change.text.length === 1) {
            const code = change.text.charCodeAt(0);
            if (code >= 0xd800 && code <= 0xdbff) {
                return;
            }
        }

        app.ReplaceText(change.replacePrevious, change.replaceNext, change.text);
        // The application's caret has moved, so the mirror is refilled around its new position.
        sync();
    });

    return { sync, place, claimsKey, endComposition, get composing() { return composing; } };
}
