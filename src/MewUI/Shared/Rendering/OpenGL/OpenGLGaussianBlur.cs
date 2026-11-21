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

    // Full-screen quad vertex buffer, shared by every context in the share group.
    private static uint _vbo;

    // Per-context objects: the quad's vertex array and the scratch FBO for the intermediate
    // (horizontal-pass output) buffer, which is recreated when the target size changes.
    private static readonly Dictionary<nint, ContextState> _contextStates = new();

    private sealed class ContextState
    {
        public uint Vao;
        public readonly RenderTarget Scratch = new();
        // The two targets a reduced blur bounces between at its lower resolution.
        public readonly RenderTarget ReducedFirst = new();
        public readonly RenderTarget ReducedSecond = new();
    }

    /// <summary>A texture with the framebuffer that draws into it.</summary>
    private sealed class RenderTarget
    {
        public uint Fbo;
        public uint Tex;
        public int Width;
        public int Height;
    }

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

        uint vbo = 0;
        OpenGLExt.GenBuffers(1, &vbo);
        if (vbo == 0)
        {
            return false;
        }

        OpenGLExt.BindBuffer(OpenGLExt.GL_ARRAY_BUFFER, vbo);
        fixed (float* p = verts)
        {
            OpenGLExt.BufferData(OpenGLExt.GL_ARRAY_BUFFER, sizeof(float) * 8, p, OpenGLExt.GL_STATIC_DRAW);
        }
        OpenGLExt.BindBuffer(OpenGLExt.GL_ARRAY_BUFFER, 0);

        _vbo = vbo;
        return true;
    }

    private static ContextState CurrentState()
    {
        nint key = CurrentContextKey();
        lock (_lock)
        {
            if (!_contextStates.TryGetValue(key, out var state))
            {
                state = new ContextState();
                _contextStates.Add(key, state);
            }

            return state;
        }
    }

    private static bool EnsureVertexArray(ContextState state)
    {
        if (state.Vao != 0)
        {
            return true;
        }

        uint vao = 0;
        OpenGLExt.GenVertexArrays(1, &vao);
        if (vao == 0)
        {
            return false;
        }

        OpenGLExt.BindVertexArray(vao);
        OpenGLExt.BindBuffer(OpenGLExt.GL_ARRAY_BUFFER, _vbo);
        OpenGLExt.EnableVertexAttribArray(0);
        OpenGLExt.VertexAttribPointer(0, 2, OpenGLExt.GL_FLOAT, normalized: false, stride: sizeof(float) * 2, pointer: null);
        OpenGLExt.BindVertexArray(0);
        OpenGLExt.BindBuffer(OpenGLExt.GL_ARRAY_BUFFER, 0);
        state.Vao = vao;
        return true;
    }

    /// <summary>
    /// Identifies the GL context current on this thread. Vertex arrays and framebuffers are not shared between
    /// contexts, so they are kept per context while programs, buffers and textures stay shared.
    /// </summary>
    private static nint CurrentContextKey()
    {
#if MEWUI_OPENGL_WIN32
        return OpenGL32.wglGetCurrentContext();
#elif MEWUI_OPENGL_X11
        return X11GLBackendRegistry.Current?.GetCurrentContext() ?? 0;
#elif MEWUI_OPENGL_EGL
        return LibEgl.eglGetCurrentContext();
#else
        return 0;
#endif
    }

    private static bool EnsureScratch(ContextState state, int width, int height) => EnsureTarget(state.Scratch, width, height);

    private static bool EnsureTarget(RenderTarget target, int width, int height)
    {
        // Exact match required - the blur shader uses v_uv ∈ [0,1] to sample the entire
        // scratch texture in pass 2. If we reused an oversized scratch (e.g. previous frame
        // was zoomed in) the stale border outside the current write region would alias into
        // pass 2's samples, producing visible "ghost" copies of older zoom levels.
        if (target.Tex != 0 && target.Fbo != 0 && target.Width == width && target.Height == height)
        {
            return true;
        }

        // (Re)allocate to fit the requested extent.
        if (target.Tex != 0)
        {
            uint t = target.Tex;
            GL.DeleteTextures(1, ref t);
            target.Tex = 0;
        }
        if (target.Fbo != 0)
        {
            uint f = target.Fbo;
            OpenGLExt.DeleteFramebuffers(1, &f);
            target.Fbo = 0;
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

        target.Tex = tex;
        target.Fbo = fbo;
        target.Width = width;
        target.Height = height;
        return true;
    }

    // A reduced blur keeps this much sigma in its own pixels, as the Metal path does.
    private const double REDUCED_SIGMA = 2.0;
    private const int MIN_REDUCED_SIDE = 8;

    /// <summary>How many times smaller to blur: none below a sigma of 2.5 pixels, else down to a sigma of about two.</summary>
    private static int ReductionFactor(double sigma, int width, int height)
    {
        if (sigma < 2.5)
        {
            return 1;
        }

        int factor = (int)Math.Floor(sigma / REDUCED_SIGMA);
        while (factor > 1 && (width / factor < MIN_REDUCED_SIDE || height / factor < MIN_REDUCED_SIDE))
        {
            factor--;
        }

        return factor;
    }

    /// <summary>
    /// Shrinks the source by <paramref name="factor"/>, blurs it there at the sigma scaled down to match, and
    /// stretches the result back into the destination; with an output region, only what reaches it is worked out.
    /// </summary>
    private static bool TryApplyReduced(ContextState state, OpenGLPixelRenderSurface source, OpenGLPixelRenderSurface dest,
        int w, int h, double sigmaX, double sigmaY, Rect? outputPixels, int factor)
    {
        int reducedWidth = Math.Max(1, w / factor);
        int reducedHeight = Math.Max(1, h / factor);
        if (!EnsureTarget(state.ReducedFirst, reducedWidth, reducedHeight) || !EnsureTarget(state.ReducedSecond, reducedWidth, reducedHeight))
        {
            return false;
        }

        var (wxArr, rx) = BuildKernel(sigmaX / factor);
        var (wyArr, ry) = BuildKernel(sigmaY / factor);
        var output = outputPixels is Rect asked ? PixelSpan.Within(asked, w, h) : new PixelSpan(0, 0, w, h);
        bool scissored = outputPixels != null;
        // The reduced pixels under the output, with one more for the stretch back to sample between.
        double scaleX = (double)reducedWidth / w;
        double scaleY = (double)reducedHeight / h;
        var reducedOutput = PixelSpan.Within(
            new Rect(output.Left * scaleX, output.Top * scaleY, (output.Right - output.Left) * scaleX, (output.Bottom - output.Top) * scaleY),
            reducedWidth, reducedHeight).Grown(1, 1, reducedWidth, reducedHeight);
        var verticalInput = reducedOutput.Grown(0, ry, reducedWidth, reducedHeight);
        var shrunk = verticalInput.Grown(rx, 0, reducedWidth, reducedHeight);

        int prevFbo = GL.GetInteger(OpenGLExt.GL_FRAMEBUFFER_BINDING);
        OpenGLExt.UseProgram(_program);
        OpenGLExt.BindVertexArray(state.Vao);
        OpenGLExt.ActiveTexture(OpenGLExt.GL_TEXTURE0);
        OpenGLExt.Uniform1i(_uTex, 0);
        if (scissored)
        {
            GL.Enable(GL.GL_SCISSOR_TEST);
        }

        var identity = new[] { 1f };
        var first = state.ReducedFirst;
        var second = state.ReducedSecond;
        // Shrink, blur across, blur down, stretch back; bilinear sampling does the shrinking and the stretching.
        BlurPass(source.Texture, first.Fbo, reducedWidth, reducedHeight, 0f, 0f, identity, 0, scissored ? shrunk : null);
        BlurPass(first.Tex, second.Fbo, reducedWidth, reducedHeight, 1f / reducedWidth, 0f, wxArr, rx, scissored ? verticalInput : null);
        BlurPass(second.Tex, first.Fbo, reducedWidth, reducedHeight, 0f, 1f / reducedHeight, wyArr, ry, scissored ? reducedOutput : null);
        BlurPass(first.Tex, dest.Fbo, w, h, 0f, 0f, identity, 0, scissored ? output : null);

        if (scissored)
        {
            GL.Disable(GL.GL_SCISSOR_TEST);
        }

        OpenGLExt.BindVertexArray(0);
        OpenGLExt.UseProgram(0);
        OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, (uint)prevFbo);
        dest.RequestDeferredReadback();
        dest.IncrementVersion();
        return true;
    }

    /// <summary>A rectangle of surface pixels, counted top-down.</summary>
    private readonly record struct PixelSpan(int Left, int Top, int Right, int Bottom)
    {
        /// <summary>The whole pixels <paramref name="area"/> touches, inside a surface of the given size.</summary>
        internal static PixelSpan Within(Rect area, int width, int height)
            => new(
                Math.Clamp((int)Math.Floor(area.X), 0, width),
                Math.Clamp((int)Math.Floor(area.Y), 0, height),
                Math.Clamp((int)Math.Ceiling(area.Right), 0, width),
                Math.Clamp((int)Math.Ceiling(area.Bottom), 0, height));

        internal PixelSpan Grown(int horizontal, int vertical, int width, int height)
            => new(
                Math.Max(0, Left - horizontal),
                Math.Max(0, Top - vertical),
                Math.Min(width, Right + horizontal),
                Math.Min(height, Bottom + vertical));
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
    /// <para>
    /// <paramref name="outputPixels"/>, in the surfaces' top-down pixels, limits the result to the pixels the
    /// caller will use; the blur still reads the source around them, and leaves the rest of the destination as it was.
    /// <paramref name="reduce"/> lets a wide blur run at a lower resolution and be stretched back, for a caller that
    /// draws the result as it is rather than feeding it to filters that read single pixels.
    /// </para>
    /// </summary>
    public static bool TryApply(OpenGLPixelRenderSurface source, OpenGLPixelRenderSurface dest,
        double sigmaX, double sigmaY, Rect? outputPixels = null, bool reduce = false)
    {
        if (source.Texture == 0 || dest.Fbo == 0 || dest.Texture == 0) return false;
        if (sigmaX <= 0 && sigmaY <= 0)
        {
            // No-op blur: if dest != source, copy source.tex → dest.fbo so caller still gets a result.
            if (!ReferenceEquals(source, dest))
            {
                if (!EnsureInitialized()) return false;
                var copyState = CurrentState();
                if (!EnsureVertexArray(copyState)) return false;
                int prevCopyFbo = GL.GetInteger(OpenGLExt.GL_FRAMEBUFFER_BINDING);
                OpenGLExt.UseProgram(_program);
                OpenGLExt.BindVertexArray(copyState.Vao);
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
        var state = CurrentState();
        int factor = reduce ? ReductionFactor(Math.Max(sigmaX, sigmaY), w, h) : 1;
        if (factor > 1)
        {
            return EnsureVertexArray(state) && TryApplyReduced(state, source, dest, w, h, sigmaX, sigmaY, outputPixels, factor);
        }

        if (!EnsureVertexArray(state) || !EnsureScratch(state, w, h)) return false;

        // Snapshot only the active FBO. NVG re-sets viewport at the next BeginFrame, so
        // letting it drift here is harmless - but a stale FBO binding could cause an
        // outer NVG pass to flush into the wrong target.
        int prevFbo = GL.GetInteger(OpenGLExt.GL_FRAMEBUFFER_BINDING);

        var (wxArr, rx) = BuildKernel(sigmaX);
        var (wyArr, ry) = BuildKernel(sigmaY);

        OpenGLExt.UseProgram(_program);
        OpenGLExt.BindVertexArray(state.Vao);
        OpenGLExt.ActiveTexture(OpenGLExt.GL_TEXTURE0);
        OpenGLExt.Uniform1i(_uTex, 0);

        bool needHorizontal = sigmaX > 0;
        bool needVertical = sigmaY > 0;

        // The second pass writes only the pixels asked for; the first one writes them and the rows the second
        // pass reads around them. Without a region both cover the whole surface.
        var whole = new PixelSpan(0, 0, w, h);
        var output = outputPixels is Rect asked ? PixelSpan.Within(asked, w, h) : whole;
        var firstPass = needVertical ? output.Grown(0, ry, w, h) : output;
        bool scissored = outputPixels != null;
        if (scissored)
        {
            GL.Enable(GL.GL_SCISSOR_TEST);
        }

        if (needHorizontal && needVertical)
        {
            // Pass 1: horizontal source.tex → scratch.fbo
            BlurPass(source.Texture, state.Scratch.Fbo, w, h, dirX: 1f / w, dirY: 0f, wxArr, rx, scissored ? firstPass : null);
            // Pass 2: vertical scratch.tex → dest.fbo
            BlurPass(state.Scratch.Tex, dest.Fbo, w, h, dirX: 0f, dirY: 1f / h, wyArr, ry, scissored ? output : null);
        }
        else if (needHorizontal)
        {
            // Horizontal only: source.tex → scratch.fbo, then identity copy → dest.fbo.
            BlurPass(source.Texture, state.Scratch.Fbo, w, h, dirX: 1f / w, dirY: 0f, wxArr, rx, scissored ? firstPass : null);
            BlurPass(state.Scratch.Tex, dest.Fbo, w, h, dirX: 0f, dirY: 0f, new[] { 1f }, 0, scissored ? output : null);
        }
        else
        {
            // Vertical only.
            BlurPass(source.Texture, state.Scratch.Fbo, w, h, dirX: 0f, dirY: 1f / h, wyArr, ry, scissored ? output : null);
            BlurPass(state.Scratch.Tex, dest.Fbo, w, h, dirX: 0f, dirY: 0f, new[] { 1f }, 0, scissored ? output : null);
        }

        if (scissored)
        {
            GL.Disable(GL.GL_SCISSOR_TEST);
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

    private static void BlurPass(uint srcTex, uint dstFbo, int w, int h, float dirX, float dirY, float[] weights, int radius, PixelSpan? scissor = null)
    {
        OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, dstFbo);
        GL.Viewport(0, 0, w, h);
        if (scissor is PixelSpan area)
        {
            // The surfaces keep their rows bottom-up, so a top-down span counts its rows from the bottom here.
            GL.Scissor(area.Left, h - area.Bottom, area.Right - area.Left, area.Bottom - area.Top);
        }

        GL.Disable(GL.GL_BLEND);
        GL.BindTexture(GL.GL_TEXTURE_2D, srcTex);

        OpenGLExt.Uniform2f(_uDir, dirX, dirY);
        OpenGLExt.Uniform1i(_uRadius, radius);
        OpenGLExt.Uniform1fv(_uWeights, weights);

        OpenGLExt.DrawArrays(OpenGLExt.GL_TRIANGLE_STRIP, 0, 4);
    }
}
