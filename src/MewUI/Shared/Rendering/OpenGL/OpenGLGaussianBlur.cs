using Aprillz.MewUI.Native;

namespace Aprillz.MewUI.Rendering.OpenGL;

/// <summary>
/// GPU separable Gaussian blur for <see cref="OpenGLPixelRenderSurface"/>. Uses a
/// 2-pass GLSL shader (horizontal then vertical), bouncing off a shared scratch FBO
/// sized to the largest target seen so far. Compiled once per process and reused.
/// </summary>
internal static unsafe class OpenGLGaussianBlur
{
    /// <summary>Maximum kernel radius supported by the shader. Raised from 32 to 256 to
    /// match Metal MPS's effectively-unlimited kernel - at high zoom (σ_pixel ≥ 32/3 ≈ 10.67)
    /// the previous cap truncated the Gaussian and made GL look noticeably less blurred than
    /// Metal for the same input. GLSL 330 fragment uniform float arrays are guaranteed up to
    /// 1024 elements so a 257-entry weights array stays well within spec.</summary>
    private const int MaxRadius = 256;

    private static readonly object _lock = new();
    private static bool _initialized;
    private static bool _available;

    // Compiled program & locations.
    private static uint _program;
    private static int _uTex;
    private static int _uDir;
    private static int _uRadius;
    private static int _uWeights;

    // Full-screen quad VAO/VBO.
    private static uint _vao;
    private static uint _vbo;

    // Scratch FBO for the intermediate (horizontal-pass output) buffer. Sized to
    // the largest target seen; texture is recreated on demand if a bigger one comes.
    private static uint _scratchFbo;
    private static uint _scratchTex;
    private static int _scratchW;
    private static int _scratchH;

    // GLSL 1.40 (OpenGL 3.1): the lowest version that has in/out, texture() and user-defined fragment
    // outputs, so the same shader compiles on a 3.1 GLX context (e.g. Mesa/d3d12 under WSL, which caps at
    // GLSL 1.40) and on the 3.3+ contexts used elsewhere. 1.40 has no layout(location=) qualifier; the
    // single vertex attribute is reliably assigned location 0 by the linker (matched in TryCreateQuad).
    private const string VertexShaderSource = @"#version 140
in vec2 a_pos;
out vec2 v_uv;
void main() {
    v_uv = a_pos * 0.5 + 0.5;
    gl_Position = vec4(a_pos, 0.0, 1.0);
}";

    // Fragment: variable-radius separable Gaussian. Weights array is symmetric
    // around index 0 (passed by absolute index). Loop bound is MaxRadius (compile-time
    // constant) - the actual iteration count is u_radius via early break, so the GPU
    // pays only for the kernel actually requested.
    private static readonly string FragmentShaderSource = $@"#version 140
in vec2 v_uv;
out vec4 fragColor;
uniform sampler2D u_tex;
uniform vec2 u_dir;             // (1/srcW, 0) horizontal, (0, 1/srcH) vertical
uniform int u_radius;           // [0..{MaxRadius}]
uniform float u_weights[{MaxRadius + 1}];   // u_weights[i] = G(i), symmetric
void main() {{
    vec4 sum = texture(u_tex, v_uv) * u_weights[0];
    for (int i = 1; i <= {MaxRadius}; i++) {{
        if (i > u_radius) break;
        vec2 off = u_dir * float(i);
        sum += (texture(u_tex, v_uv + off) + texture(u_tex, v_uv - off)) * u_weights[i];
    }}
    fragColor = sum;
}}";

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
        uint vs = CompileShader(OpenGLExt.GL_VERTEX_SHADER, VertexShaderSource);
        if (vs == 0) return false;

        uint fs = CompileShader(OpenGLExt.GL_FRAGMENT_SHADER, FragmentShaderSource);
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
        _uDir = OpenGLExt.GetUniformLocation(prog, "u_dir");
        _uRadius = OpenGLExt.GetUniformLocation(prog, "u_radius");
        _uWeights = OpenGLExt.GetUniformLocation(prog, "u_weights");
        return true;
    }

    private static uint CompileShader(uint stage, string source)
    {
        uint shader = OpenGLExt.CreateShader(stage);
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
        // Two-triangle strip covering NDC.
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
        OpenGLExt.EnableVertexAttribArray(0);
        OpenGLExt.VertexAttribPointer(0, 2, OpenGLExt.GL_FLOAT, normalized: false, stride: sizeof(float) * 2, pointer: null);
        OpenGLExt.BindVertexArray(0);
        OpenGLExt.BindBuffer(OpenGLExt.GL_ARRAY_BUFFER, 0);

        _vao = vao;
        _vbo = vbo;
        return true;
    }

    private static bool EnsureScratch(int width, int height)
    {
        // Exact match required - the blur shader uses v_uv ∈ [0,1] to sample the entire
        // scratch texture in pass 2. If we reused an oversized scratch (e.g. previous frame
        // was zoomed in) the stale border outside the current write region would alias into
        // pass 2's samples, producing visible "ghost" copies of older zoom levels.
        if (_scratchTex != 0 && _scratchFbo != 0 && _scratchW == width && _scratchH == height)
        {
            return true;
        }

        // (Re)allocate to fit the requested extent.
        if (_scratchTex != 0)
        {
            uint t = _scratchTex;
            GL.DeleteTextures(1, ref t);
            _scratchTex = 0;
        }
        if (_scratchFbo != 0)
        {
            uint f = _scratchFbo;
            OpenGLExt.DeleteFramebuffers(1, &f);
            _scratchFbo = 0;
        }

        GL.GenTextures(1, out uint tex);
        if (tex == 0) return false;
        GL.BindTexture(GL.GL_TEXTURE_2D, tex);
        GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_MIN_FILTER, (int)GL.GL_LINEAR);
        GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_MAG_FILTER, (int)GL.GL_LINEAR);
        GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_WRAP_S, (int)GL.GL_CLAMP_TO_EDGE);
        GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_WRAP_T, (int)GL.GL_CLAMP_TO_EDGE);
        GL.TexImage2D(GL.GL_TEXTURE_2D, 0, (int)GL.GL_RGBA, width, height, 0,
            GL.GL_RGBA, GL.GL_UNSIGNED_BYTE, 0);
        GL.BindTexture(GL.GL_TEXTURE_2D, 0);

        uint fbo = 0;
        OpenGLExt.GenFramebuffers(1, &fbo);
        if (fbo == 0)
        {
            GL.DeleteTextures(1, ref tex);
            return false;
        }
        OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, fbo);
        OpenGLExt.FramebufferTexture2D(OpenGLExt.GL_FRAMEBUFFER, OpenGLExt.GL_COLOR_ATTACHMENT0,
            GL.GL_TEXTURE_2D, tex, 0);
        uint status = OpenGLExt.CheckFramebufferStatus(OpenGLExt.GL_FRAMEBUFFER);
        OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, 0);
        if (status != OpenGLExt.GL_FRAMEBUFFER_COMPLETE)
        {
            uint f = fbo;
            OpenGLExt.DeleteFramebuffers(1, &f);
            GL.DeleteTextures(1, ref tex);
            return false;
        }

        _scratchTex = tex;
        _scratchFbo = fbo;
        _scratchW = width;
        _scratchH = height;
        return true;
    }

    private static (float[] weights, int radius) BuildKernel(double sigma)
    {
        if (sigma <= 0) return (new[] { 1f }, 0);
        int radius = Math.Min(MaxRadius, Math.Max(1, (int)Math.Ceiling(sigma * 3.0)));
        var w = new float[radius + 1];
        double twoSigmaSq = 2.0 * sigma * sigma;
        double sum = 0;
        for (int i = 0; i <= radius; i++)
        {
            double v = Math.Exp(-(i * i) / twoSigmaSq);
            w[i] = (float)v;
            // weight for absolute offset: counted twice except i=0
            sum += i == 0 ? v : 2.0 * v;
        }
        for (int i = 0; i <= radius; i++) w[i] = (float)(w[i] / sum);
        return (w, radius);
    }

    /// <summary>
    /// Applies a separable Gaussian blur from <paramref name="source"/>'s GPU texture into
    /// <paramref name="dest"/>'s FBO. Pass <c>source == dest</c> for in-place blur.
    /// <para>
    /// Caller must ensure the GL context owning both targets' FBO/texture is current and
    /// that <paramref name="dest"/>'s FBO is initialized (call <c>InitializeFbo</c> if not).
    /// </para>
    /// Returns false when the shader pipeline is unavailable, program creation failed, or
    /// either target has no GPU resources.
    /// </summary>
    public static bool TryApply(OpenGLPixelRenderSurface source, OpenGLPixelRenderSurface dest,
        double sigmaX, double sigmaY)
    {
        if (source.Texture == 0 || dest.Fbo == 0 || dest.Texture == 0) return false;
        if (sigmaX <= 0 && sigmaY <= 0)
        {
            // No-op blur: if dest != source, copy source.tex → dest.fbo so caller still gets a result.
            if (!ReferenceEquals(source, dest))
            {
                if (!EnsureInitialized()) return false;
                int prevCopyFbo = GL.GetInteger(OpenGLExt.GL_FRAMEBUFFER_BINDING);
                OpenGLExt.UseProgram(_program);
                OpenGLExt.BindVertexArray(_vao);
                OpenGLExt.ActiveTexture(OpenGLExt.GL_TEXTURE0);
                OpenGLExt.Uniform1i(_uTex, 0);
                BlurPass(source.Texture, dest.Fbo, dest.PixelWidth, dest.PixelHeight, 0, 0, new[] { 1f }, 0);
                OpenGLExt.BindVertexArray(0);
                OpenGLExt.UseProgram(0);
                OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, (uint)prevCopyFbo);
                // Deferred readback - the immediate version was the per-filter sync barrier
                // that turned 100 small filters into a 1+ second frame. The CPU mirror is
                // populated lazily when something actually reads it.
                dest.RequestDeferredReadback();
                dest.IncrementVersion();
            }
            return true;
        }
        if (!EnsureInitialized()) return false;

        int w = Math.Min(source.PixelWidth, dest.PixelWidth);
        int h = Math.Min(source.PixelHeight, dest.PixelHeight);
        if (!EnsureScratch(w, h)) return false;

        // Snapshot only the active FBO. NVG re-sets viewport at the next BeginFrame, so
        // letting it drift here is harmless - but a stale FBO binding could cause an
        // outer NVG pass to flush into the wrong target.
        int prevFbo = GL.GetInteger(OpenGLExt.GL_FRAMEBUFFER_BINDING);

        var (wxArr, rx) = BuildKernel(sigmaX);
        var (wyArr, ry) = BuildKernel(sigmaY);

        OpenGLExt.UseProgram(_program);
        OpenGLExt.BindVertexArray(_vao);
        OpenGLExt.ActiveTexture(OpenGLExt.GL_TEXTURE0);
        OpenGLExt.Uniform1i(_uTex, 0);

        bool needHorizontal = sigmaX > 0;
        bool needVertical = sigmaY > 0;

        if (needHorizontal && needVertical)
        {
            // Pass 1: horizontal source.tex → scratch.fbo
            BlurPass(source.Texture, _scratchFbo, w, h, dirX: 1f / w, dirY: 0f, wxArr, rx);
            // Pass 2: vertical scratch.tex → dest.fbo
            BlurPass(_scratchTex, dest.Fbo, w, h, dirX: 0f, dirY: 1f / h, wyArr, ry);
        }
        else if (needHorizontal)
        {
            // Horizontal only: source.tex → scratch.fbo, then identity copy → dest.fbo.
            BlurPass(source.Texture, _scratchFbo, w, h, dirX: 1f / w, dirY: 0f, wxArr, rx);
            BlurPass(_scratchTex, dest.Fbo, w, h, dirX: 0f, dirY: 0f, new[] { 1f }, 0);
        }
        else
        {
            // Vertical only.
            BlurPass(source.Texture, _scratchFbo, w, h, dirX: 0f, dirY: 1f / h, wyArr, ry);
            BlurPass(_scratchTex, dest.Fbo, w, h, dirX: 0f, dirY: 0f, new[] { 1f }, 0);
        }

        OpenGLExt.BindVertexArray(0);
        OpenGLExt.UseProgram(0);
        OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, (uint)prevFbo);

        // Deferred readback - see the early-return path's note. The first CPU consumer
        // (Lock / CopyPixels / GetPixelSpan on dest) triggers a single glReadPixels then.
        dest.RequestDeferredReadback();
        dest.IncrementVersion();

        return true;
    }

    private static void BlurPass(uint srcTex, uint dstFbo, int w, int h, float dirX, float dirY, float[] weights, int radius)
    {
        OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, dstFbo);
        GL.Viewport(0, 0, w, h);
        GL.Disable(GL.GL_BLEND);
        GL.BindTexture(GL.GL_TEXTURE_2D, srcTex);

        OpenGLExt.Uniform2f(_uDir, dirX, dirY);
        OpenGLExt.Uniform1i(_uRadius, radius);
        OpenGLExt.Uniform1fv(_uWeights, weights);

        OpenGLExt.DrawArrays(OpenGLExt.GL_TRIANGLE_STRIP, 0, 4);
    }
}
