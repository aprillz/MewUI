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
    }
});

// Measures one line in CSS pixels. Ascent and descent are written through the out pointers.
EM_JS(double, mewui_text_measure, (const char* utf8_text, const char* utf8_font, double* out_ascent, double* out_descent), {
    mewui_text_ensure_context();
    var ctx = Module.mewuiTextCtx;
    ctx.font = UTF8ToString(utf8_font);
    ctx.textBaseline = "alphabetic";
    var metrics = ctx.measureText(UTF8ToString(utf8_text));
    if (out_ascent) HEAPF64[out_ascent >> 3] = metrics.fontBoundingBoxAscent || 0;
    if (out_descent) HEAPF64[out_descent >> 3] = metrics.fontBoundingBoxDescent || 0;
    return metrics.width;
});

// Rasterizes wrapped, aligned text into straight-alpha RGBA. Returns the line count actually drawn.
EM_JS(int, mewui_text_rasterize, (const char* utf8_text, const char* utf8_font, int width_px, int height_px,
    double scale, int red, int green, int blue, int alpha, int h_align, int v_align, int wrap,
    unsigned char* out_pixels), {
    mewui_text_ensure_context();
    var canvas = Module.mewuiTextCanvas;
    var ctx = Module.mewuiTextCtx;
    if (canvas.width < width_px || canvas.height < height_px)
    {
        canvas.width = Math.max(canvas.width, width_px);
        canvas.height = Math.max(canvas.height, height_px);
    }

    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, width_px, height_px);
    ctx.font = UTF8ToString(utf8_font);
    ctx.textBaseline = "alphabetic";
    ctx.fillStyle = "rgba(" + red + "," + green + "," + blue + "," + (alpha / 255) + ")";

    var text = UTF8ToString(utf8_text);
    var metrics = ctx.measureText("Mg");
    var ascent = metrics.fontBoundingBoxAscent || 0;
    var descent = metrics.fontBoundingBoxDescent || 0;
    var lineHeight = (ascent + descent) * scale;
    var maxWidth = width_px;

    var lines = [];
    var paragraphs = text.split("\n");
    for (var p = 0; p < paragraphs.length; p++)
    {
        if (wrap === 0 || paragraphs[p].length === 0)
        {
            lines.push(paragraphs[p]);
            continue;
        }

        // Greedy word wrap; falls back to per-character breaks for words wider than the box.
        var words = paragraphs[p].split(" ");
        var current = "";
        for (var w = 0; w < words.length; w++)
        {
            var candidate = current.length === 0 ? words[w] : current + " " + words[w];
            if (ctx.measureText(candidate).width * scale <= maxWidth || current.length === 0)
            {
                current = candidate;
            }
            else
            {
                lines.push(current);
                current = words[w];
            }
        }
        lines.push(current);
    }

    var totalHeight = lines.length * lineHeight;
    var originY = 0;
    if (v_align === 1) originY = (height_px - totalHeight) / 2;
    else if (v_align === 2) originY = height_px - totalHeight;

    ctx.scale(scale, scale);
    for (var i = 0; i < lines.length; i++)
    {
        var lineWidth = ctx.measureText(lines[i]).width * scale;
        var originX = 0;
        if (h_align === 1) originX = (maxWidth - lineWidth) / 2;
        else if (h_align === 2) originX = maxWidth - lineWidth;
        ctx.fillText(lines[i], originX / scale, (originY + i * lineHeight) / scale + ascent);
    }

    // Reading only the drawn band keeps the readback proportional to the text, not to the canvas
    // the browser grew to the widest run ever rasterized.
    var image = ctx.getImageData(0, 0, width_px, height_px);
    HEAPU8.set(image.data, out_pixels);
    return lines.length;
});
