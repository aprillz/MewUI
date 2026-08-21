using Aprillz.MewUI.Native;

namespace Aprillz.MewUI.Rendering.OpenGL;

/// <summary>
/// GPU passes for the non-blur filter nodes on <see cref="OpenGLPixelRenderSurface"/>: a
/// placed textured quad that can optionally run a 4x5 color matrix, drawn either opaquely
/// or source-over. One program, compiled once per process and reused.
/// </summary>
internal static unsafe class OpenGLFilterPasses
{
    private static readonly object _lock = new();
    private static bool _initialized;
    private static bool _available;

    private static uint _program;
    private static int _uTex;
    private static int _uRect;
    private static int _uUseMatrix;
    private static int _uMatrix;

    private static uint _vao;
    private static uint _vbo;

    // GLSL 1.40 for the same reason as the blur shader: it is the lowest version with in/out
    // and user-defined fragment outputs, so one source compiles on 3.1 and 3.3+ contexts.
    private const string VERTEX_SHADER_SOURCE = @"#version 140
in vec2 a_pos;
out vec2 v_uv;
uniform float u_rect[4];        // destination quad in NDC: x0, y0, x1, y1
void main() {
    v_uv = a_pos * 0.5 + 0.5;
    vec2 p = vec2(mix(u_rect[0], u_rect[2], v_uv.x), mix(u_rect[1], u_rect[3], v_uv.y));
    gl_Position = vec4(p, 0.0, 1.0);
}";

    // u_matrix is the row-major 4x5 SVG color matrix. Samples arrive premultiplied, so the
    // pass un-premultiplies, transforms, clamps, and premultiplies again - matching the CPU
    // executor, which does the same around its ApplyColorMatrix.
    private const string FRAGMENT_SHADER_SOURCE = @"#version 140
in vec2 v_uv;
out vec4 fragColor;
uniform sampler2D u_tex;
uniform int u_useMatrix;
uniform float u_matrix[20];
void main() {
    vec4 c = texture(u_tex, v_uv);
    if (u_useMatrix == 0) {
        fragColor = c;
        return;
    }
    vec4 s = c.a > 0.0 ? vec4(c.rgb / c.a, c.a) : vec4(0.0);
    vec4 r;
    r.r = u_matrix[0] * s.r + u_matrix[1] * s.g + u_matrix[2] * s.b + u_matrix[3] * s.a + u_matrix[4];
    r.g = u_matrix[5] * s.r + u_matrix[6] * s.g + u_matrix[7] * s.b + u_matrix[8] * s.a + u_matrix[9];
    r.b = u_matrix[10] * s.r + u_matrix[11] * s.g + u_matrix[12] * s.b + u_matrix[13] * s.a + u_matrix[14];
    r.a = u_matrix[15] * s.r + u_matrix[16] * s.g + u_matrix[17] * s.b + u_matrix[18] * s.a + u_matrix[19];
    r = clamp(r, 0.0, 1.0);
    fragColor = vec4(r.rgb * r.a, r.a);
}";

    private static readonly float[] _identityMatrix =
    {
        1, 0, 0, 0, 0,
        0, 1, 0, 0, 0,
        0, 0, 1, 0, 0,
        0, 0, 0, 1, 0,
    };

    /// <summary>One input of <see cref="TryComposite"/>, placed at a pixel offset from the
    /// destination's top-left.</summary>
    internal readonly struct CompositeLayer(OpenGLPixelRenderSurface surface, int offsetX, int offsetY)
    {
        public OpenGLPixelRenderSurface Surface { get; } = surface;
        public int OffsetX { get; } = offsetX;
        public int OffsetY { get; } = offsetY;
    }

    /// <summary>Copies <paramref name="source"/> into <paramref name="dest"/> while applying a
    /// row-major 4x5 color matrix. Both targets must belong to the current GL context and
    /// <paramref name="dest"/> must have an initialized FBO.</summary>
    public static bool TryApplyColorMatrix(OpenGLPixelRenderSurface source, OpenGLPixelRenderSurface dest, float[] matrix)
    {
        if (matrix.Length != 20 || source.Texture == 0)
        {
            return false;
        }

        return TryDraw(dest, () =>
        {
            GL.Disable(GL.GL_BLEND);
            ClearTarget();
            DrawQuad(source.Texture, 0, 0, dest.PixelWidth, dest.PixelHeight, dest.PixelWidth, dest.PixelHeight, matrix);
        });
    }

    /// <summary>Copies <paramref name="source"/> into <paramref name="dest"/> unchanged.</summary>
    public static bool TryCopy(OpenGLPixelRenderSurface source, OpenGLPixelRenderSurface dest)
    {
        if (source.Texture == 0)
        {
            return false;
        }

        return TryDraw(dest, () =>
        {
            GL.Disable(GL.GL_BLEND);
            ClearTarget();
            DrawQuad(source.Texture, 0, 0, dest.PixelWidth, dest.PixelHeight, dest.PixelWidth, dest.PixelHeight, null);
        });
    }

    /// <summary>Source-over composites <paramref name="layers"/> into <paramref name="dest"/>,
    /// the first layer at the bottom. Each layer is placed at its own pixel offset inside the
    /// destination so inputs an offset node moved stay aligned.</summary>
    public static bool TryComposite(OpenGLPixelRenderSurface dest, IReadOnlyList<CompositeLayer> layers)
    {
        foreach (var layer in layers)
        {
            if (layer.Surface.Texture == 0)
            {
                return false;
            }
        }

        return TryDraw(dest, () =>
        {
            ClearTarget();
            GL.Enable(GL.GL_BLEND);
            // Inputs are premultiplied, so source-over is ONE / ONE_MINUS_SRC_ALPHA.
            GL.BlendFunc(GL.GL_ONE, GL.GL_ONE_MINUS_SRC_ALPHA);
            foreach (var layer in layers)
            {
                DrawQuad(layer.Surface.Texture, layer.OffsetX, layer.OffsetY,
                    layer.Surface.PixelWidth, layer.Surface.PixelHeight,
                    dest.PixelWidth, dest.PixelHeight, null);
            }
            GL.Disable(GL.GL_BLEND);
        });
    }

    private static bool TryDraw(OpenGLPixelRenderSurface dest, Action body)
    {
        if (dest.Fbo == 0 || dest.Texture == 0 || !EnsureInitialized())
        {
            return false;
        }

        // Snapshot only the active FBO, as the blur pass does: the next BeginFrame resets the
        // viewport anyway, but a stale FBO binding would send an outer pass to the wrong target.
        int prevFbo = GL.GetInteger(OpenGLExt.GL_FRAMEBUFFER_BINDING);

        OpenGLExt.UseProgram(_program);
        OpenGLExt.BindVertexArray(_vao);
        OpenGLExt.ActiveTexture(OpenGLExt.GL_TEXTURE0);
        OpenGLExt.Uniform1i(_uTex, 0);
        OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, dest.Fbo);
        GL.Viewport(0, 0, dest.PixelWidth, dest.PixelHeight);

        body();

        OpenGLExt.BindVertexArray(0);
        OpenGLExt.UseProgram(0);
        OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, (uint)prevFbo);

        // Deferred readback for the same reason as the blur pass: reading back per node turns a
        // graph of small filters into a chain of GPU stalls.
        dest.RequestDeferredReadback();
        dest.IncrementVersion();
        return true;
    }

    private static void ClearTarget()
    {
        GL.ClearColor(0f, 0f, 0f, 0f);
        GL.Clear(GL.GL_COLOR_BUFFER_BIT);
    }

    private static void DrawQuad(uint texture, int offsetX, int offsetY, int width, int height, int destWidth, int destHeight, float[]? matrix)
    {
        // Offsets arrive in image space (Y down) while an FBO's rows run bottom-up, so the
        // vertical placement is mirrored: v=0 samples the source's bottom row and must land on
        // the bottom of the placed rectangle.
        float x0 = (offsetX / (float)destWidth * 2f) - 1f;
        float x1 = ((offsetX + width) / (float)destWidth * 2f) - 1f;
        float y0 = ((destHeight - offsetY - height) / (float)destHeight * 2f) - 1f;
        float y1 = ((destHeight - offsetY) / (float)destHeight * 2f) - 1f;

        Span<float> rect = stackalloc float[] { x0, y0, x1, y1 };
        OpenGLExt.Uniform1fv(_uRect, rect);
        OpenGLExt.Uniform1i(_uUseMatrix, matrix is null ? 0 : 1);
        OpenGLExt.Uniform1fv(_uMatrix, matrix ?? _identityMatrix);

        GL.BindTexture(GL.GL_TEXTURE_2D, texture);
        OpenGLExt.DrawArrays(OpenGLExt.GL_TRIANGLE_STRIP, 0, 4);
    }

    private static bool EnsureInitialized()
    {
        if (_initialized)
        {
            return _available;
        }

        lock (_lock)
        {
            if (_initialized)
            {
                return _available;
            }

            _initialized = true;
            if (!OpenGLExt.IsShaderPipelineSupported)
            {
                _available = false;
                return false;
            }

            _available = TryCreateProgram() && TryCreateQuad();
            return _available;
        }
    }

    private static bool TryCreateProgram()
    {
        uint vs = CompileShader(OpenGLExt.GL_VERTEX_SHADER, VERTEX_SHADER_SOURCE);
        if (vs == 0) return false;

        uint fs = CompileShader(OpenGLExt.GL_FRAGMENT_SHADER, FRAGMENT_SHADER_SOURCE);
        if (fs == 0) { OpenGLExt.DeleteShader(vs); return false; }

        uint prog = OpenGLExt.CreateProgram();
        if (prog == 0) { OpenGLExt.DeleteShader(vs); OpenGLExt.DeleteShader(fs); return false; }

        OpenGLExt.AttachShader(prog, vs);
        OpenGLExt.AttachShader(prog, fs);
        OpenGLExt.LinkProgram(prog);
        OpenGLExt.DeleteShader(vs);
        OpenGLExt.DeleteShader(fs);

        if (OpenGLExt.GetProgramiv(prog, OpenGLExt.GL_LINK_STATUS) == 0)
        {
            OpenGLExt.DeleteProgram(prog);
            return false;
        }

        _program = prog;
        _uTex = OpenGLExt.GetUniformLocation(prog, "u_tex");
        _uRect = OpenGLExt.GetUniformLocation(prog, "u_rect[0]");
        _uUseMatrix = OpenGLExt.GetUniformLocation(prog, "u_useMatrix");
        _uMatrix = OpenGLExt.GetUniformLocation(prog, "u_matrix[0]");
        return true;
    }

    private static uint CompileShader(uint type, string source)
    {
        uint shader = OpenGLExt.CreateShader(type);
        if (shader == 0) return 0;

        OpenGLExt.ShaderSource(shader, source);
        OpenGLExt.CompileShader(shader);
        if (OpenGLExt.GetShaderiv(shader, OpenGLExt.GL_COMPILE_STATUS) == 0)
        {
            OpenGLExt.DeleteShader(shader);
            return 0;
        }
        return shader;
    }

    private static bool TryCreateQuad()
    {
        Span<float> verts = stackalloc float[8] { -1, -1, 1, -1, -1, 1, 1, 1 };

        uint vao = 0, vbo = 0;
        OpenGLExt.GenVertexArrays(1, &vao);
        OpenGLExt.GenBuffers(1, &vbo);
        if (vao == 0 || vbo == 0)
        {
            if (vao != 0) OpenGLExt.DeleteVertexArrays(1, &vao);
            if (vbo != 0) OpenGLExt.DeleteBuffers(1, &vbo);
            return false;
        }

        OpenGLExt.BindVertexArray(vao);
        OpenGLExt.BindBuffer(OpenGLExt.GL_ARRAY_BUFFER, vbo);
        fixed (float* p = verts)
        {
            OpenGLExt.BufferData(OpenGLExt.GL_ARRAY_BUFFER, sizeof(float) * 8, p, OpenGLExt.GL_STATIC_DRAW);
        }
        // GLSL 1.40 has no layout(location=) qualifier; the single attribute lands on 0.
        OpenGLExt.EnableVertexAttribArray(0);
        OpenGLExt.VertexAttribPointer(0, 2, OpenGLExt.GL_FLOAT, normalized: false, stride: sizeof(float) * 2, pointer: null);
        OpenGLExt.BindVertexArray(0);
        OpenGLExt.BindBuffer(OpenGLExt.GL_ARRAY_BUFFER, 0);

        _vao = vao;
        _vbo = vbo;
        return true;
    }
}
