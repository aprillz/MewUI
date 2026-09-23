#include <emscripten/em_js.h>
#include <emscripten/html5.h>
#include <emscripten/html5_webgl.h>
#include <GLES3/gl3.h>

static EMSCRIPTEN_WEBGL_CONTEXT_HANDLE mewui_context;

int mewui_webgl_init(const char* selector)
{
    EmscriptenWebGLContextAttributes attrs;
    emscripten_webgl_init_context_attributes(&attrs);
    attrs.majorVersion = 2;
    attrs.minorVersion = 0;
    attrs.alpha = 0;
    attrs.depth = 0;
    attrs.stencil = 0;
    attrs.antialias = 0;
    attrs.preserveDrawingBuffer = 0;

    mewui_context = emscripten_webgl_create_context(selector, &attrs);
    if (mewui_context <= 0)
    {
        return mewui_context != 0 ? (int)mewui_context : -1;
    }

    return (int)emscripten_webgl_make_context_current(mewui_context);
}

void* mewui_webgl_get_proc(const char* name)
{
    return emscripten_webgl_get_proc_address(name);
}

// Makes the canvas context current for the frame. Target binding, viewport and clearing are
// handled by the shared OpenGL render path.
void mewui_webgl_make_current(void)
{
    emscripten_webgl_make_context_current(mewui_context);
}

void mewui_sig_viiiiiiiii(int arg0, int arg1, int arg2, int arg3, int arg4, int arg5, int arg6, int arg7, int arg8) {}
void mewui_sig_vif(int arg0, float arg1) {}
void mewui_sig_vffff(float arg0, float arg1, float arg2, float arg3) {}

// Canvas2D text path for First Boot: the browser rasterizes glyphs into an offscreen canvas and
// MewVG uploads the result as a texture. Replaced by the FreeType/HarfBuzz path in a later phase.

EM_JS(void, mewui_text_ensure_context, (), {
    if (!Module.mewuiTextCanvas)
    {
        Module.mewuiTextCanvas = document.createElement("canvas");
        Module.mewuiTextCtx = Module.mewuiTextCanvas.getContext("2d", { willReadFrequently: true });
        Module.mewuiTextFont = null;
    }
});

// Measures one line in CSS pixels. Ascent and descent are written through the out pointers.
EM_JS(double, mewui_text_measure, (const char* utf8_text, const char* utf8_font, double* out_ascent, double* out_descent), {
    mewui_text_ensure_context();
    var ctx = Module.mewuiTextCtx;
    var f = UTF8ToString(utf8_font);
    if (Module.mewuiTextFont !== f) { ctx.font = f; ctx.textBaseline = "alphabetic"; Module.mewuiTextFont = f; }
    var metrics = ctx.measureText(UTF8ToString(utf8_text));
    if (out_ascent) HEAPF64[out_ascent >> 3] = metrics.fontBoundingBoxAscent || 0;
    if (out_descent) HEAPF64[out_descent >> 3] = metrics.fontBoundingBoxDescent || 0;
    return metrics.width;
});

// Ink box of the given text, as opposed to the font box mewui_text_measure reports. Cap height is
// not part of TextMetrics, but the ink ascent of a flat capital is exactly it. The parameter list
// matches mewui_text_measure so both reuse one interop signature.
EM_JS(double, mewui_text_ink_box, (const char* utf8_text, const char* utf8_font, double* out_ascent, double* out_descent), {
    mewui_text_ensure_context();
    var ctx = Module.mewuiTextCtx;
    var f = UTF8ToString(utf8_font);
    if (Module.mewuiTextFont !== f) { ctx.font = f; ctx.textBaseline = "alphabetic"; Module.mewuiTextFont = f; }
    var metrics = ctx.measureText(UTF8ToString(utf8_text));
    if (out_ascent) HEAPF64[out_ascent >> 3] = metrics.actualBoundingBoxAscent || 0;
    if (out_descent) HEAPF64[out_descent >> 3] = metrics.actualBoundingBoxDescent || 0;
    return metrics.width;
});

// Ink of one run past its advance box and the font's ascent/descent band, in CSS pixels: the left and
// right overhang go to out_horizontal[0..1], the top and bottom to out_vertical[0..1], measured from the
// baseline the rasterizers draw at. The parameter list matches mewui_text_measure.
EM_JS(double, mewui_text_ink_extent, (const char* utf8_text, const char* utf8_font, double* out_horizontal, double* out_vertical), {
    mewui_text_ensure_context();
    var ctx = Module.mewuiTextCtx;
    var f = UTF8ToString(utf8_font);
    if (Module.mewuiTextFont !== f) { ctx.font = f; ctx.textBaseline = "alphabetic"; Module.mewuiTextFont = f; }
    var metrics = ctx.measureText(UTF8ToString(utf8_text));
    var band = ctx.measureText("Mg");
    HEAPF64[out_horizontal >> 3] = metrics.actualBoundingBoxLeft || 0;
    HEAPF64[(out_horizontal >> 3) + 1] = (metrics.actualBoundingBoxRight || 0) - metrics.width;
    HEAPF64[out_vertical >> 3] = (metrics.actualBoundingBoxAscent || 0) - (band.fontBoundingBoxAscent || 0);
    HEAPF64[(out_vertical >> 3) + 1] = (metrics.actualBoundingBoxDescent || 0) - (band.fontBoundingBoxDescent || 0);
    return metrics.width;
});

// Advance after every UTF-16 code unit of the text, as the rasterizers lay the whole run out. A
// glyph measured on its own can come out narrower than it sits in its word (desktop Firefox does
// this by about 4%), so each word is measured in context by its prefixes and the sum is fitted to
// the run's own width. Words are capped so an unbroken run costs a bounded number of calls per
// unit. out_capacity[0] carries how many doubles out_advances holds; the parameter list matches
// mewui_text_measure. Returns the width of the whole run.
EM_JS(double, mewui_text_prefix_advances, (const char* utf8_text, const char* utf8_font, double* out_advances, double* out_capacity), {
    mewui_text_ensure_context();
    var ctx = Module.mewuiTextCtx;
    var f = UTF8ToString(utf8_font);
    if (Module.mewuiTextFont !== f) { ctx.font = f; ctx.textBaseline = "alphabetic"; Module.mewuiTextFont = f; }
    var text = UTF8ToString(utf8_text);
    var capacity = HEAPF64[out_capacity >> 3] | 0;
    var count = Math.min(text.length, capacity);
    var base = out_advances >> 3;
    var whole = ctx.measureText(text).width;
    var isSpace = function (code) { return code === 32 || code === 9 || code === 160 || code === 12288; };

    var cursor = 0;
    var start = 0;
    while (start < count)
    {
        var end = start;
        while (end < count && end - start < 48 && !isSpace(text.charCodeAt(end))) { end++; }
        while (end < count && isSpace(text.charCodeAt(end))) { end++; }
        if (end === start) { end = start + 1; }
        // A capped word must not end between the halves of a surrogate pair.
        if (end < count) { var next = text.charCodeAt(end); if (next >= 56320 && next <= 57343) { end++; } }
        for (var index = start + 1; index <= end; index++)
        {
            HEAPF64[base + index - 1] = cursor + ctx.measureText(text.substring(start, index)).width;
        }
        cursor = HEAPF64[base + end - 1];
        start = end;
    }

    if (count === text.length && cursor > 0 && whole > 0)
    {
        var fit = whole / cursor;
        for (var unit = 0; unit < count; unit++) { HEAPF64[base + unit] *= fit; }
    }

    // Marshalling stops at an embedded NUL; the units past it keep the last edge.
    for (var rest = count; rest < capacity; rest++) { HEAPF64[base + rest] = count > 0 ? HEAPF64[base + count - 1] : 0; }
    return whole;
});

// Rasterizes one text run into straight-alpha RGBA. Returns the line count drawn.
EM_JS(int, mewui_text_rasterize, (const char* utf8_text, const char* utf8_font, int width_px, int height_px,
    double scale, int red, int green, int blue, int alpha, int inset_left_px, int inset_top_px, int wrap,
    unsigned char* out_pixels), {
    mewui_text_ensure_context();
    var canvas = Module.mewuiTextCanvas;
    var ctx = Module.mewuiTextCtx;
    if (canvas.width < width_px || canvas.height < height_px)
    {
        canvas.width = Math.max(canvas.width, width_px);
        canvas.height = Math.max(canvas.height, height_px);
        // Resizing the backing store resets every context property, including the font.
        Module.mewuiTextFont = null;
    }

    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, width_px, height_px);
    var f = UTF8ToString(utf8_font);
    if (Module.mewuiTextFont !== f) { ctx.font = f; ctx.textBaseline = "alphabetic"; Module.mewuiTextFont = f; }
    ctx.fillStyle = "rgba(" + red + "," + green + "," + blue + "," + (alpha / 255) + ")";

    // The managed text engine breaks lines and positions every run, so one call draws one run at the
    // top left of its own box, inset by the room a grown texture leaves for glyph ink overhang. wrap
    // stays in the signature but carries no work.
    var ascent = ctx.measureText("Mg").fontBoundingBoxAscent || 0;
    if (!(ascent > 0))
    {
        // BrowserFont uses the CSS font size when fontBoundingBoxAscent is unavailable. Keep the
        // raster baseline on that same fallback instead of drawing alphabetic text at y = 0.
        // EM_JS drops backslashes from its body, so the pattern is written without any.
        var sizeMatch = new RegExp("(?:^| )([0-9]+(?:[.][0-9]+)?)px").exec(f);
        ascent = sizeMatch ? Number(sizeMatch[1]) : 0;
    }
    ctx.translate(inset_left_px, inset_top_px);
    ctx.scale(scale, scale);
    ctx.fillText(UTF8ToString(utf8_text), 0, ascent);

    // Reading only the drawn band keeps the readback proportional to the text, not to the canvas
    // the browser grew to the widest run ever rasterized.
    var image = ctx.getImageData(0, 0, width_px, height_px);
    HEAPU8.set(image.data, out_pixels);
    return 1;
});

// Draws one run straight into the given GL texture: the canvas is handed to texSubImage2D as the
// pixel source, so the readback and both copies the pixels used to make on their way back to the
// GPU do not happen. The parameter list matches mewui_text_rasterize exactly, unused arguments
// included, so both share one interop signature; a shape without a trampoline aborts the runtime.
EM_JS(int, mewui_text_draw_to_texture, (const char* utf8_text, const char* utf8_font, int width_px, int height_px,
    double scale, int red, int green, int blue, int alpha, int inset_left_px, int inset_top_px, int wrap,
    unsigned int texture), {
    mewui_text_ensure_context();
    var canvas = Module.mewuiTextCanvas;
    var ctx = Module.mewuiTextCtx;
    if (canvas.width < width_px || canvas.height < height_px)
    {
        canvas.width = Math.max(canvas.width, width_px);
        canvas.height = Math.max(canvas.height, height_px);
        Module.mewuiTextFont = null;
    }

    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, width_px, height_px);
    var f = UTF8ToString(utf8_font);
    if (Module.mewuiTextFont !== f) { ctx.font = f; ctx.textBaseline = "alphabetic"; Module.mewuiTextFont = f; }
    ctx.fillStyle = "rgba(" + red + "," + green + "," + blue + "," + (alpha / 255) + ")";
    var ascent = ctx.measureText("Mg").fontBoundingBoxAscent || 0;
    if (!(ascent > 0))
    {
        // EM_JS drops backslashes from its body, so the pattern is written without any.
        var sizeMatch = new RegExp("(?:^| )([0-9]+(?:[.][0-9]+)?)px").exec(f);
        ascent = sizeMatch ? Number(sizeMatch[1]) : 0;
    }
    ctx.translate(inset_left_px, inset_top_px);
    ctx.scale(scale, scale);
    ctx.fillText(UTF8ToString(utf8_text), 0, ascent);

    var handle = GL.textures[texture];
    if (!handle) { return 0; }

    // The renderer sets the unpack rows and alignment it needs before each of its own uploads, so
    // nothing has to be saved here; the two WebGL-only parameters apply to uploads from a DOM
    // source alone, which the renderer never does, and are stated so nothing is inherited. Not
    // premultiplying keeps the straight alpha the readback used to produce, so the image flags on
    // the managed side stay as they were.
    GLctx.pixelStorei(GLctx.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false);
    GLctx.pixelStorei(GLctx.UNPACK_FLIP_Y_WEBGL, false);
    GLctx.bindTexture(GLctx.TEXTURE_2D, handle);

    // Only the band the run occupies, not the whole canvas the widest run so far grew it to.
    GLctx.texSubImage2D(GLctx.TEXTURE_2D, 0, 0, 0, width_px, height_px,
        GLctx.RGBA, GLctx.UNSIGNED_BYTE, canvas);
    GLctx.bindTexture(GLctx.TEXTURE_2D, null);
    return 1;
});
