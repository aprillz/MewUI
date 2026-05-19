using System.Runtime.InteropServices;
using System.Text;

using FFmpeg.AutoGen;

using Aprillz.MewUI.Video.Sample.Diagnostics;

namespace Aprillz.MewUI.Video.Sample.Decoding;

public sealed unsafe class VideoDecoder : IDisposable
{
    private readonly nint _preferredD3D11Device;
    private AVFormatContext* _format;
    private AVCodecContext* _codec;
    private AVBufferRef* _hardwareDeviceContext;
    private SwsContext* _sws;
    private AVFrame* _frame;
    private AVFrame* _softwareFrame;
    private AVPacket* _packet;
    private readonly int _videoStreamIndex;
    private bool _reachedEof;
    private bool _disposed;
    private bool _firstDecodedFrameLogged;
    private bool _hardwareDecodeLogged;
    private bool _hardwareDecodeEnabled;
    private bool _hasMinimumOutputPts;
    private TimeSpan _minimumOutputPts;
    private AVPixelFormat _hardwarePixelFormat = AVPixelFormat.AV_PIX_FMT_NONE;
    private AVHWDeviceType _activeHardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
    private D3D11VideoProcessorConverter? _hardwareTextureConverter;
    private bool _hardwareTextureConverterInitTried;
    private bool _hardwareTextureConverterAvailable;
    private readonly string _codecName = string.Empty;
    private readonly long _bitRate;
    private readonly AVRational _averageFrameRate;
    private readonly AVRational _streamFrameRate;
    private readonly AVRational _sampleAspectRatio;
    private readonly AVRational _streamTimeBase;
    private AVPixelFormat _lastFrameFormat = AVPixelFormat.AV_PIX_FMT_NONE;
    private bool _lastDecodeWasHardwareBacked;
    private bool _lastExportedViaGpuConverter;
    private TimeSpan _lastDecodedPts;
    private volatile bool _forceCpuReadback;
    private VideoToolboxMetalBridge? _videoToolboxBridge;

    // VAAPI: scale_vaapi filter graph that converts decoded NV12 surfaces to BGRA
    // surfaces in-GPU before export. Without this the dma_buf import gets a
    // YUV-formatted surface and NVG's sampler2D path renders garbage colors.
    private AVFilterGraph* _vaapiFilterGraph;
    private AVFilterContext* _vaapiFilterSource;
    private AVFilterContext* _vaapiFilterSink;
    private AVFrame* _vaapiFilteredFrame;
    private bool _vaapiFilterInitTried;

    public VideoDecoder(string path, nint preferredD3D11Device = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        SampleLog.Write($"VideoDecoder ctor: {path}");
        _preferredD3D11Device = preferredD3D11Device;

        AVFormatContext* format = null;
        ThrowIfError(ffmpeg.avformat_open_input(&format, path, null, null), "open input");
        SampleLog.Write("avformat_open_input succeeded.");
        _format = format;

        try
        {
            ThrowIfError(ffmpeg.avformat_find_stream_info(_format, null), "find stream info");
            SampleLog.Write("avformat_find_stream_info succeeded.");

            _videoStreamIndex = ffmpeg.av_find_best_stream(_format, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
            if (_videoStreamIndex < 0)
            {
                throw new InvalidOperationException("No video stream found.");
            }
            SampleLog.Write($"Selected video stream index: {_videoStreamIndex}");

            var stream = _format->streams[_videoStreamIndex];
            var codecPar = stream->codecpar;
            var codec = ffmpeg.avcodec_find_decoder(codecPar->codec_id);
            if (codec is null)
            {
                throw new InvalidOperationException($"Unsupported codec: {codecPar->codec_id}.");
            }

            _codec = ffmpeg.avcodec_alloc_context3(codec);
            if (_codec is null)
            {
                throw new InvalidOperationException("Failed to allocate codec context.");
            }

            ThrowIfError(ffmpeg.avcodec_parameters_to_context(_codec, codecPar), "copy codec parameters");
            _codecName = Marshal.PtrToStringAnsi((nint)codec->name) ?? codecPar->codec_id.ToString();
            _bitRate = codecPar->bit_rate > 0 ? codecPar->bit_rate : _format->bit_rate;
            _averageFrameRate = stream->avg_frame_rate;
            _streamFrameRate = stream->r_frame_rate;
            _sampleAspectRatio = stream->sample_aspect_ratio;
            _streamTimeBase = stream->time_base;
            TryEnableHardwareDecode(codec);
            ThrowIfError(ffmpeg.avcodec_open2(_codec, codec, null), "open codec");
            LogHardwareDecodeSetup(codec);

            _frame = ffmpeg.av_frame_alloc();
            _softwareFrame = ffmpeg.av_frame_alloc();
            _packet = ffmpeg.av_packet_alloc();
            _vaapiFilteredFrame = ffmpeg.av_frame_alloc();
            if (_frame is null || _softwareFrame is null || _packet is null || _vaapiFilteredFrame is null)
            {
                throw new InvalidOperationException("Failed to allocate FFmpeg frame/packet buffers.");
            }

            Width = _codec->width;
            Height = _codec->height;
            TimeBase = stream->time_base;
            Duration = GetDuration(stream, _format);

            // Wire up the NV12→BGRA scale_vaapi filter so the decoded surface is
            // already in the format NVG expects when the display side imports it
            // via dma_buf. The filter is opaque (graph alloc / parsed config) so
            // failure just falls through to the existing CPU path. The VideoView
            // catches the missing GpuResource and samples from the decoded pixel buffer.
            if (OperatingSystem.IsLinux() && _hardwareDecodeEnabled
                && _activeHardwareDeviceType == AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI)
            {
                TryInitVaapiFilterGraph();
            }
            SampleLog.Write($"Decoder ready. codec={codecPar->codec_id}, size={Width}x{Height}, duration={Duration}");
        }
        catch
        {
            SampleLog.Write("VideoDecoder ctor failed. Disposing decoder state.");
            Dispose();
            throw;
        }
    }

    public int Width { get; }

    public int Height { get; }

    public TimeSpan Duration { get; }

    public AVRational TimeBase { get; }

    /// <summary>
    /// Metal device (<c>id&lt;MTLDevice&gt;</c>) the VideoToolbox bridge was created on.
    /// Returns 0 on non-macOS platforms or before the first hardware frame is decoded.
    /// </summary>
    public nint MetalDevice => _videoToolboxBridge?.MetalDevice ?? 0;

    /// <summary>
    /// Native <c>ID3D11Device*</c> the FFmpeg D3D11VA hardware decoder is bound to.
    /// Returns 0 when hardware decode isn't active. Used by downstream interop
    /// (WGL_NV_DX_interop, DXGI shared NT handle import) to register the decoded
    /// textures with another GPU API.
    /// </summary>
    public nint D3D11Device
    {
        get
        {
            if (_hardwareDeviceContext is null) return 0;
            // AVHWDeviceContext layout: data->hwctx is AVD3D11VADeviceContext*, whose
            // first field is `ID3D11Device *device`. Reading the first pointer-sized
            // slot gives us the device handle without binding to FFmpeg.AutoGen's
            // typed wrapper (which may differ across versions).
            var hwDeviceContext = (AVHWDeviceContext*)_hardwareDeviceContext->data;
            if (hwDeviceContext is null || hwDeviceContext->hwctx is null) return 0;
            return *(nint*)hwDeviceContext->hwctx;
        }
    }

    /// <summary>
    /// Linux VA-API <c>VADisplay</c> handle the FFmpeg VAAPI hardware decoder is
    /// bound to. Returns 0 when VAAPI hardware decode isn't active. Used by the
    /// dma_buf zero-copy export path (<c>VaapiDmaBufTexture</c>).
    /// </summary>
    public nint VaDisplay
    {
        get
        {
            if (_hardwareDeviceContext is null) return 0;
            // AVVAAPIDeviceContext layout: { VADisplay display; ... }. The VADisplay
            // is the first pointer-sized field of hwctx — same memory pattern as the
            // D3D11 device above, just a different opaque type.
            var hwDeviceContext = (AVHWDeviceContext*)_hardwareDeviceContext->data;
            if (hwDeviceContext is null || hwDeviceContext->hwctx is null) return 0;
            return *(nint*)hwDeviceContext->hwctx;
        }
    }

    public bool TryDecodeNext(Span<byte> bgraBuffer, out TimeSpan pts, out IGpuFrameResource? gpuResource, out bool hasCpuPixels)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        gpuResource = null;
        hasCpuPixels = false;

        int requiredLength = checked(Width * Height * 4);
        if (bgraBuffer.Length < requiredLength)
        {
            throw new ArgumentException($"Buffer length must be at least {requiredLength} bytes.", nameof(bgraBuffer));
        }

        while (true)
        {
            int receiveResult = ffmpeg.avcodec_receive_frame(_codec, _frame);
            if (receiveResult == 0)
            {
                LogActiveDecodePath(_frame);

                AVFrame* hwFrameForExport = _frame;
                bool forceCpuReadback = _forceCpuReadback;
                bool isHardwareFrame = _frame->hw_frames_ctx is not null
                    || (_hardwareDecodeEnabled && (AVPixelFormat)_frame->format == _hardwarePixelFormat);
                bool exportedViaGpuConverter = false;
                _lastFrameFormat = (AVPixelFormat)_frame->format;
                _lastDecodeWasHardwareBacked = isHardwareFrame || _codec->hw_device_ctx is not null;

                if (isHardwareFrame && !forceCpuReadback)
                {
                    if (OperatingSystem.IsMacOS())
                    {
                        // VideoToolbox path: data[3] is a CVPixelBufferRef (BGRA or NV12,
                        // IOSurface-backed). Wrap as MTLTexture via CVMetalTextureCache for
                        // zero-copy sampling. VideoToolboxGpuResource.Dispose() releases the
                        // CVMetalTextureRef and flushes the cache so stale IOSurface entries
                        // are reclaimed immediately when the frame is recycled.
                        var vtTexture = TryWrapVideoToolboxFrame(_frame);
                        if (vtTexture is not null)
                        {
                            gpuResource = new VideoToolboxGpuResource(vtTexture, _videoToolboxBridge!);
                            exportedViaGpuConverter = true;
                        }
                    }
                    else if (OperatingSystem.IsLinux() && _activeHardwareDeviceType == AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI)
                    {
                        // VAAPI path: push the decoded NV12 surface through the
                        // scale_vaapi filter so the surface we hand off is already
                        // BGRA. CUDA/NVDEC frames skip this and fall through to
                        // av_hwframe_transfer_data + sws (NV12→BGRA on CPU).
                        AVFrame* outFrame = ProcessVaapiFilter(_frame);
                        if (outFrame is not null)
                        {
                            nint vaDisplay = VaDisplay;
                            nint surfaceField = (nint)outFrame->data[3];
                            if (vaDisplay != 0 && surfaceField != 0)
                            {
                                gpuResource = new VaapiGpuResource(vaDisplay, (uint)surfaceField);
                                hwFrameForExport = outFrame; // keep export pointing at the BGRA frame
                                exportedViaGpuConverter = true;
                            }
                        }
                    }
                    else if (TryConvertHardwareFrameForInterop(_frame, out nint convertedHandle, out int convertedSubresource))
                    {
                        gpuResource = new D3D11GpuResource(convertedHandle, convertedSubresource, D3D11Device, ReturnConvertedTexture);
                        ((D3D11GpuResource)gpuResource).SetRasterSize(Width, Height);
                        exportedViaGpuConverter = true;
                    }
                }
                _lastExportedViaGpuConverter = exportedViaGpuConverter;

                if (!exportedViaGpuConverter)
                {
                    if (!forceCpuReadback && _activeHardwareDeviceType == AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA)
                    {
                        // D3D11VA-only: data[0] is an ID3D11Texture2D* (COM). For CUDA/
                        // VAAPI/VideoToolbox it's a device pointer or opaque handle —
                        // calling Marshal.AddRef on those segfaults.
                        CaptureHardwareFrameMetadata(hwFrameForExport, out nint rawHandle, out int rawSubresource, out bool rawHardwareDecoded);
                        if (rawHardwareDecoded && rawHandle != 0)
                        {
                            gpuResource = new D3D11GpuResource(rawHandle, rawSubresource, D3D11Device, static handle => Marshal.Release(handle));
                            ((D3D11GpuResource)gpuResource).SetRasterSize(Width, Height);
                        }
                    }

                    AVFrame* convertedFrame = GetFrameForColorConversion(_frame);
                    ConvertCurrentFrame(convertedFrame, bgraBuffer);
                    hasCpuPixels = true;
                }

                long timestamp = _frame->best_effort_timestamp != ffmpeg.AV_NOPTS_VALUE
                    ? _frame->best_effort_timestamp
                    : _frame->pts;

                pts = TimestampToTimeSpan(timestamp, TimeBase);
                _lastDecodedPts = pts;
                if (_hasMinimumOutputPts && pts < _minimumOutputPts)
                {
                    gpuResource?.Dispose();
                    gpuResource = null;
                    ffmpeg.av_frame_unref(_softwareFrame);
                    ffmpeg.av_frame_unref(_frame);
                    continue;
                }

                _hasMinimumOutputPts = false;
                if (!_firstDecodedFrameLogged)
                {
                    _firstDecodedFrameLogged = true;
                    SampleLog.Write($"First decoded frame ready. pts={pts}");
                }

                ffmpeg.av_frame_unref(_softwareFrame);
                ffmpeg.av_frame_unref(_frame);
                return true;
            }

            if (receiveResult == ffmpeg.AVERROR(ffmpeg.EAGAIN))
            {
                int readResult = ffmpeg.av_read_frame(_format, _packet);
                if (readResult == ffmpeg.AVERROR_EOF)
                {
                    if (_reachedEof)
                    {
                        pts = default;
                        return false;
                    }

                    _reachedEof = true;
                    int flushResult = ffmpeg.avcodec_send_packet(_codec, null);
                    if (flushResult < 0 && flushResult != ffmpeg.AVERROR_EOF && flushResult != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                    {
                        ThrowIfError(flushResult, "flush decoder");
                    }

                    continue;
                }

                ThrowIfError(readResult, "read frame");

                if (_packet->stream_index != _videoStreamIndex)
                {
                    ffmpeg.av_packet_unref(_packet);
                    continue;
                }

                int sendResult = ffmpeg.avcodec_send_packet(_codec, _packet);
                ffmpeg.av_packet_unref(_packet);

                if (sendResult == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                {
                    continue;
                }

                ThrowIfError(sendResult, "send packet");
                continue;
            }

            if (receiveResult == ffmpeg.AVERROR_EOF)
            {
                pts = default;
                return false;
            }

            ThrowIfError(receiveResult, "receive frame");
        }
    }

    public void Seek(TimeSpan target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SampleLog.Write($"Decoder seek: {target}");

        long targetTimestamp = TimeSpanToTimestamp(target, TimeBase);
        ThrowIfError(ffmpeg.av_seek_frame(_format, _videoStreamIndex, targetTimestamp, ffmpeg.AVSEEK_FLAG_BACKWARD), "seek frame");
        ffmpeg.avcodec_flush_buffers(_codec);
        ffmpeg.av_frame_unref(_frame);
        ffmpeg.av_frame_unref(_softwareFrame);
        ffmpeg.av_packet_unref(_packet);
        _reachedEof = false;
        _minimumOutputPts = target;
        _hasMinimumOutputPts = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        SampleLog.Write("Disposing VideoDecoder.");
        _disposed = true;

        if (_packet is not null)
        {
            fixed (AVPacket** packet = &_packet)
            {
                ffmpeg.av_packet_free(packet);
            }
        }

        if (_frame is not null)
        {
            fixed (AVFrame** frame = &_frame)
            {
                ffmpeg.av_frame_free(frame);
            }
        }

        if (_softwareFrame is not null)
        {
            fixed (AVFrame** frame = &_softwareFrame)
            {
                ffmpeg.av_frame_free(frame);
            }
        }

        if (_vaapiFilteredFrame is not null)
        {
            fixed (AVFrame** frame = &_vaapiFilteredFrame)
            {
                ffmpeg.av_frame_free(frame);
            }
        }

        if (_vaapiFilterGraph is not null)
        {
            fixed (AVFilterGraph** graph = &_vaapiFilterGraph)
            {
                ffmpeg.avfilter_graph_free(graph);
            }
            _vaapiFilterSource = null;
            _vaapiFilterSink = null;
        }

        _videoToolboxBridge?.Dispose();
        _videoToolboxBridge = null;
        _hardwareTextureConverter?.Dispose();
        _hardwareTextureConverter = null;

        if (_codec is not null)
        {
            fixed (AVCodecContext** codec = &_codec)
            {
                ffmpeg.avcodec_free_context(codec);
            }
        }

        if (_hardwareDeviceContext is not null)
        {
            fixed (AVBufferRef** hardwareDeviceContext = &_hardwareDeviceContext)
            {
                ffmpeg.av_buffer_unref(hardwareDeviceContext);
            }
        }

        if (_format is not null)
        {
            fixed (AVFormatContext** format = &_format)
            {
                ffmpeg.avformat_close_input(format);
            }
        }

        if (_sws is not null)
        {
            ffmpeg.sws_freeContext(_sws);
            _sws = null;
        }
    }

    public string GetStatsOverlayText()
    {
        var builder = new StringBuilder();
        builder.Append("ffmpeg\n");
        builder.Append("codec: ").Append(_codecName);
        builder.Append("\nsize: ").Append(Width).Append('x').Append(Height);

        double fps = RationalToDouble(_averageFrameRate);
        if (fps <= 0)
        {
            fps = RationalToDouble(_streamFrameRate);
        }

        if (fps > 0)
        {
            builder.Append(" @ ").Append(fps.ToString("0.###")).Append(" fps");
        }

        if (_bitRate > 0)
        {
            builder.Append("\nbitrate: ").Append(FormatBitRate(_bitRate));
        }

        builder.Append("\npts: ").Append(_lastDecodedPts.ToString(@"hh\:mm\:ss\.fff"));
        builder.Append("\ntime base: ").Append(FormatRational(_streamTimeBase));
        builder.Append("\nsar: ").Append(FormatRational(_sampleAspectRatio));
        builder.Append("\navg/r fps: ")
            .Append(FormatDouble(RationalToDouble(_averageFrameRate)))
            .Append(" / ")
            .Append(FormatDouble(RationalToDouble(_streamFrameRate)));

        string hwLabel = _activeHardwareDeviceType switch
        {
            AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX => "videotoolbox",
            AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI => "vaapi",
            AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA => "nvdec",
            AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA => "d3d11",
            _ => OperatingSystem.IsMacOS() ? "videotoolbox" : "d3d11",
        };
        builder.Append("\ndecode: ").Append(_lastDecodeWasHardwareBacked ? hwLabel : "software");
        builder.Append("\nframe fmt: ").Append(_lastFrameFormat);
        builder.Append("\ncodec fmt: ").Append(_codec->pix_fmt);
        builder.Append("\nhw pix fmt: ").Append(_hardwarePixelFormat);
        builder.Append("\nhw ctx: ").Append(_hardwareDeviceContext != null ? "set" : "null");
        builder.Append("\ninterop convert: ").Append(_lastExportedViaGpuConverter ? "gpu" : "fallback");
        builder.Append("\nconverter: display-owned");
        builder.Append("\ncpu readback policy: ").Append(_forceCpuReadback ? "forced" : "allowed");
        return builder.ToString();
    }

    public void EnableCpuReadbackFallback()
    {
        _forceCpuReadback = true;
    }

    /// <summary>
    /// When <see langword="true"/>, the decoder skips every zero-copy GPU-export
    /// path (D3D11 interop, VAAPI dma_buf, VideoToolbox/Metal) and exposes only
    /// CPU-readback frames through <see cref="IPixelBufferSource"/>. Used for
    /// benchmarking the upload-side codepath (sync upload vs async PBO+fence) on
    /// real video without depending on driver-specific zero-copy availability.
    /// </summary>
    /// <remarks>
    /// Toggle is observed at the next decoded frame — no need to recreate the
    /// decoder. Hardware decode itself stays active (the HW frame is just
    /// transferred to system memory via <c>av_hwframe_transfer_data</c>); only
    /// the GPU-side wrap is skipped.
    /// </remarks>
    public bool ForceCpuReadback
    {
        get => _forceCpuReadback;
        set => _forceCpuReadback = value;
    }

    /// <summary>
    /// macOS zero-copy path: wraps a VideoToolbox-decoded AVFrame's CVPixelBuffer as a
    /// disposable <see cref="VideoToolboxFrameTexture"/> (which exposes both an
    /// <c>MTLTexture</c> handle and the <see cref="IExternalRasterSource"/> contract
    /// MewVG Metal needs for NoDelete-style sampling).
    /// </summary>
    /// <remarks>
    /// On VideoToolbox-decoded frames FFmpeg stores the <c>CVPixelBufferRef</c> in
    /// <c>frame->data[3]</c>. The bridge is created lazily on the first wrap so the
    /// system Metal device is only touched when there's actually a frame to display.
    /// Returns null when the bridge / cache could not be initialised — caller then falls
    /// back to the CPU readback path.
    /// </remarks>
    private VideoToolboxFrameTexture? TryWrapVideoToolboxFrame(AVFrame* frame)
    {
        nint cvPixelBuffer = (nint)frame->data[3];
        if (cvPixelBuffer == 0)
        {
            return null;
        }

        if (_videoToolboxBridge is null)
        {
            nint device = CoreVideoInterop.MTLCreateSystemDefaultDevice();
            if (device == 0)
            {
                SampleLog.Write("VideoToolbox bridge: MTLCreateSystemDefaultDevice returned 0; falling back to CPU readback.");
                return null;
            }

            try
            {
                _videoToolboxBridge = new VideoToolboxMetalBridge(device);
            }
            catch (Exception ex)
            {
                SampleLog.Write($"VideoToolbox bridge init failed: {ex.Message}. Falling back to CPU readback.");
                CoreVideoInterop.CFRelease(device);
                return null;
            }
        }

        // Bridge handles BGRA direct-wrap and NV12 → BGRA via VTPixelTransferSession
        // internally. Returns null on unsupported formats / GPU failures; caller falls
        // back to CPU readback.
        return _videoToolboxBridge.TryWrap(cvPixelBuffer);
    }

    private bool TryConvertHardwareFrameForInterop(AVFrame* hwFrame, out nint d3d11TextureHandle, out int d3d11SubresourceIndex)
    {
        d3d11TextureHandle = 0;
        d3d11SubresourceIndex = 0;

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        nint inputTexture = (nint)hwFrame->data[0];
        if (inputTexture == 0 || D3D11Device == 0)
        {
            return false;
        }

        if (!_hardwareTextureConverterInitTried)
        {
            _hardwareTextureConverterInitTried = true;
            _hardwareTextureConverterAvailable = D3D11VideoProcessorConverter.TryCreate(D3D11Device, out _hardwareTextureConverter);
            SampleLog.Write(_hardwareTextureConverterAvailable
                ? "D3D11 video processor interop converter initialised."
                : "D3D11 video processor interop converter unavailable; exposing decoder surface directly.");
        }

        if (!_hardwareTextureConverterAvailable || _hardwareTextureConverter is null)
        {
            return false;
        }

        if (!_hardwareTextureConverter.TryConvert(inputTexture, unchecked((int)(nint)hwFrame->data[1]), Width, Height, out d3d11TextureHandle))
        {
            SampleLog.Write("D3D11 video processor conversion failed; exposing decoder surface directly.");
            return false;
        }

        d3d11SubresourceIndex = 0;
        return true;
    }

    private void ReturnConvertedTexture(nint texture)
        => _hardwareTextureConverter?.ReturnOutputTexture(texture);

    private void TryEnableHardwareDecode(AVCodec* codec)
    {
        if (OperatingSystem.IsMacOS())
        {
            TryEnableVideoToolboxDecode(codec);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            TryEnableVaapiDecode(codec);
            if (!_hardwareDecodeEnabled)
            {
                TryEnableCudaDecode(codec);
            }
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var hardwareConfig = FindHardwareConfig(codec, AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA);
        if (hardwareConfig is null)
        {
            SampleLog.Write("D3D11VA hardware decode not available for this codec. Falling back to software decode.");
            return;
        }

        _hardwarePixelFormat = hardwareConfig->pix_fmt;

        // Try creating our own D3D11 device with BGRA_SUPPORT first. FFmpeg's default
        // av_hwdevice_ctx_create only enables VIDEO_SUPPORT on some paths, so we prefer
        // a BGRA-capable device up front for sample-side GPU interop conversion. If the
        // manual creation fails, fall back to FFmpeg's built-in path and let CPU
        // sws_scale handle presentation fallback.
        if (TryCreateInteropReadyD3D11Device())
        {
            _hardwareDeviceContext = ffmpeg.av_buffer_ref(_codec->hw_device_ctx);
            _hardwareDecodeEnabled = _hardwareDeviceContext is not null;
            if (_hardwareDecodeEnabled)
            {
                _activeHardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA;
            }
            SampleLog.Write($"D3D11VA configured with interop-ready device. hw_pix_fmt={_hardwarePixelFormat}");
            return;
        }

        int createResult = ffmpeg.av_hwdevice_ctx_create(&_codec->hw_device_ctx, AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, null, null, 0);
        if (createResult < 0 || _codec->hw_device_ctx is null)
        {
            SampleLog.Write($"D3D11VA device creation failed: {FormatError(createResult)}. Falling back to software decode.");
            return;
        }

        _hardwareDeviceContext = ffmpeg.av_buffer_ref(_codec->hw_device_ctx);
        _hardwareDecodeEnabled = _hardwareDeviceContext is not null;
        if (_hardwareDecodeEnabled)
        {
            _activeHardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA;
        }
        SampleLog.Write($"D3D11VA configured with FFmpeg default device. hw_pix_fmt={_hardwarePixelFormat}");
    }

    /// <summary>
    /// macOS HW decode path. VideoToolbox is the system framework Apple ships for
    /// hardware-accelerated H.264/HEVC/etc. decode — FFmpeg wraps it as
    /// <c>AV_HWDEVICE_TYPE_VIDEOTOOLBOX</c>, which decodes into a CVPixelBuffer
    /// (NV12 by default, IOSurface-backed). Display-side zero-copy then wraps the
    /// IOSurface as an MTLTexture without touching CPU memory.
    /// </summary>
    /// <remarks>
    /// On failure the caller stays on the software decode path (no exception bubbled).
    /// FFmpeg's <c>av_hwframe_transfer_data</c> handles the CPU readback fallback for
    /// VideoToolbox transparently, so the existing color-conversion pipeline works
    /// even when the display side hasn't yet picked up the zero-copy wrap.
    /// </remarks>
    private void TryEnableVideoToolboxDecode(AVCodec* codec)
    {
        var hardwareConfig = FindHardwareConfig(codec, AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX);
        if (hardwareConfig is null)
        {
            SampleLog.Write("VideoToolbox hardware decode not available for this codec. Falling back to software decode.");
            return;
        }

        _hardwarePixelFormat = hardwareConfig->pix_fmt;

        int createResult = ffmpeg.av_hwdevice_ctx_create(
            &_codec->hw_device_ctx,
            AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX,
            null,
            null,
            0);
        if (createResult < 0 || _codec->hw_device_ctx is null)
        {
            SampleLog.Write($"VideoToolbox device creation failed: {FormatError(createResult)}. Falling back to software decode.");
            return;
        }

        _hardwareDeviceContext = ffmpeg.av_buffer_ref(_codec->hw_device_ctx);
        _hardwareDecodeEnabled = _hardwareDeviceContext is not null;
        if (_hardwareDecodeEnabled)
        {
            _activeHardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX;
        }

        // Pre-create hw_frames_ctx with sw_format=BGRA so VideoToolbox is configured to
        // emit single-plane BGRA8 CVPixelBuffers. Default would be NV12 (two-plane YUV),
        // which can't be sampled directly by NanoVG's RGBA shader without an extra
        // conversion pass. With BGRA we get an IOSurface-backed CVPixelBuffer that wraps
        // straight into a single MTLTexture for zero-copy display.
        bool framesCtxOk = TryConfigureBgraHwFramesContext(codecPar: _codec);
        SampleLog.Write($"VideoToolbox hw_frames_ctx pre-config (sw_format=BGRA): {(framesCtxOk ? "ok" : "failed — VT will use default NV12")}");

        SampleLog.Write($"VideoToolbox configured. hw_pix_fmt={_hardwarePixelFormat}");
    }

    /// <summary>
    /// Linux HW decode path. VA-API (Video Acceleration API) is the standard Linux
    /// interface for hardware decode on Intel iHD / Mesa radeonsi / Nouveau / NVDEC.
    /// FFmpeg wraps it as <c>AV_HWDEVICE_TYPE_VAAPI</c>, which decodes into a VASurface
    /// (typically NV12, DRM-backed). Display-side zero-copy then exports the VASurface
    /// as a DRM PRIME dma_buf and imports into a GL texture via
    /// <c>EGL_LINUX_DMA_BUF_EXT</c> + <c>glEGLImageTargetTexture2DOES</c>.
    /// </summary>
    /// <remarks>
    /// On failure the caller stays on the software decode path. NVIDIA proprietary
    /// drivers don't ship VA-API; users on those need <c>libnvidia-vaapi-driver</c> or
    /// the path falls through to FFmpeg software decode.
    /// </remarks>
    private void TryEnableVaapiDecode(AVCodec* codec)
    {
        var hardwareConfig = FindHardwareConfig(codec, AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI);
        if (hardwareConfig is null)
        {
            SampleLog.Write("VA-API hardware decode not available for this codec. Falling back to software decode.");
            return;
        }

        _hardwarePixelFormat = hardwareConfig->pix_fmt;

        // Pass null device — FFmpeg picks the default DRM render node (/dev/dri/renderD128).
        // Apps that need a specific GPU can extend this to honor an env var.
        int createResult = ffmpeg.av_hwdevice_ctx_create(
            &_codec->hw_device_ctx,
            AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI,
            null,
            null,
            0);
        if (createResult < 0 || _codec->hw_device_ctx is null)
        {
            SampleLog.Write($"VA-API device creation failed: {FormatError(createResult)}. Falling back to software decode.");
            return;
        }

        _hardwareDeviceContext = ffmpeg.av_buffer_ref(_codec->hw_device_ctx);
        _hardwareDecodeEnabled = _hardwareDeviceContext is not null;
        if (_hardwareDecodeEnabled)
        {
            _activeHardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI;
        }

        SampleLog.Write($"VA-API configured. hw_pix_fmt={_hardwarePixelFormat}");
    }

    /// <summary>
    /// Linux NVDEC path. Used when VA-API is unavailable (WSL, NVIDIA proprietary
    /// driver without libnvidia-vaapi-driver, etc.). NVDEC decodes into a CUDA
    /// device surface (AV_PIX_FMT_CUDA); we let av_hwframe_transfer_data copy it
    /// to NV12 in system memory and the existing sws path converts to BGRA.
    /// </summary>
    private void TryEnableCudaDecode(AVCodec* codec)
    {
        var hardwareConfig = FindHardwareConfig(codec, AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA);
        if (hardwareConfig is null)
        {
            SampleLog.Write("CUDA/NVDEC hardware decode not available for this codec.");
            return;
        }

        _hardwarePixelFormat = hardwareConfig->pix_fmt;

        int createResult = ffmpeg.av_hwdevice_ctx_create(
            &_codec->hw_device_ctx,
            AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA,
            null,
            null,
            0);
        if (createResult < 0 || _codec->hw_device_ctx is null)
        {
            SampleLog.Write($"CUDA device creation failed: {FormatError(createResult)}. Falling back to software decode.");
            return;
        }

        _hardwareDeviceContext = ffmpeg.av_buffer_ref(_codec->hw_device_ctx);
        _hardwareDecodeEnabled = _hardwareDeviceContext is not null;
        if (_hardwareDecodeEnabled)
        {
            _activeHardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA;
        }

        SampleLog.Write($"CUDA/NVDEC configured. hw_pix_fmt={_hardwarePixelFormat}");
    }

    /// <summary>
    /// Build a <c>buffer → scale_vaapi=format=bgra → buffersink</c> graph that
    /// converts decoded NV12 hardware surfaces to BGRA hardware surfaces, so the
    /// downstream dma_buf export hands NVG a sampler2D-compatible RGBA texture
    /// instead of an opaque YUV blob.
    /// </summary>
    /// <remarks>
    /// Failure is silent (logged only): if any filter alloc / config call fails,
    /// <see cref="_vaapiFilterGraph"/> stays null and <see cref="ProcessVaapiFilter"/>
    /// short-circuits, leaving the decoder on the existing NV12 surface path. The
    /// VideoView Linux branch then either still attempts the dma_buf import (and
    /// shows wrong colors — useful for debugging) or falls back to CPU upload.
    /// </remarks>
    private void TryInitVaapiFilterGraph()
    {
        if (_vaapiFilterInitTried) return;
        _vaapiFilterInitTried = true;

        if (_codec->hw_frames_ctx is null)
        {
            // The decoder hasn't allocated a hw_frames_ctx yet. avcodec_open2 only
            // creates one when the first packet is decoded for some codecs. We'll
            // retry on the first frame in TryDecodeNext.
            SampleLog.Write("VAAPI filter init deferred — codec has no hw_frames_ctx yet.");
            return;
        }

        AVFilterGraph* graph = ffmpeg.avfilter_graph_alloc();
        if (graph is null)
        {
            SampleLog.Write("VAAPI filter init: avfilter_graph_alloc failed.");
            return;
        }

        try
        {
            // buffer source filter — feeds decoded VAAPI frames into the graph.
            var bufferFilter = ffmpeg.avfilter_get_by_name("buffer");
            var bufferSinkFilter = ffmpeg.avfilter_get_by_name("buffersink");
            if (bufferFilter is null || bufferSinkFilter is null)
            {
                SampleLog.Write("VAAPI filter init: buffer/buffersink filters not registered.");
                ffmpeg.avfilter_graph_free(&graph);
                return;
            }

            // The buffer source needs format / dimensions / time_base. For VAAPI
            // hardware frames the pixel format is AV_PIX_FMT_VAAPI and the actual
            // surface metadata travels via hw_frames_ctx (set after creation).
            string args = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "video_size={0}x{1}:pix_fmt={2}:time_base={3}/{4}:pixel_aspect={5}/{6}",
                Width, Height,
                (int)AVPixelFormat.AV_PIX_FMT_VAAPI,
                _streamTimeBase.num, _streamTimeBase.den,
                _sampleAspectRatio.num <= 0 ? 1 : _sampleAspectRatio.num,
                _sampleAspectRatio.den <= 0 ? 1 : _sampleAspectRatio.den);

            AVFilterContext* sourceCtx = null;
            AVFilterContext* sinkCtx = null;
            int rc;

            rc = ffmpeg.avfilter_graph_create_filter(&sourceCtx, bufferFilter, "in", args, null, graph);
            if (rc < 0)
            {
                SampleLog.Write($"VAAPI filter init: create buffer source failed: {FormatError(rc)}");
                ffmpeg.avfilter_graph_free(&graph);
                return;
            }

            // Propagate hw_frames_ctx from the codec to the buffer source — this
            // tells the graph that the input surfaces are VAAPI-backed and which
            // VADisplay / surface format they live on.
            AVBufferSrcParameters* parms = ffmpeg.av_buffersrc_parameters_alloc();
            if (parms is null)
            {
                SampleLog.Write("VAAPI filter init: buffersrc_parameters_alloc failed.");
                ffmpeg.avfilter_graph_free(&graph);
                return;
            }

            parms->format = (int)AVPixelFormat.AV_PIX_FMT_VAAPI;
            parms->width = Width;
            parms->height = Height;
            parms->time_base = _streamTimeBase;
            parms->hw_frames_ctx = ffmpeg.av_buffer_ref(_codec->hw_frames_ctx);

            rc = ffmpeg.av_buffersrc_parameters_set(sourceCtx, parms);
            ffmpeg.av_free(parms);

            if (rc < 0)
            {
                SampleLog.Write($"VAAPI filter init: buffersrc_parameters_set failed: {FormatError(rc)}");
                ffmpeg.avfilter_graph_free(&graph);
                return;
            }

            rc = ffmpeg.avfilter_graph_create_filter(&sinkCtx, bufferSinkFilter, "out", null, null, graph);
            if (rc < 0)
            {
                SampleLog.Write($"VAAPI filter init: create buffersink failed: {FormatError(rc)}");
                ffmpeg.avfilter_graph_free(&graph);
                return;
            }

            // Parse the filter description: scale_vaapi performs the GPU-side
            // NV12→BGRA color conversion using the VPP block of the same VAAPI
            // device. format=bgra requests bgra output; scale_vaapi infers w/h
            // from the input when not specified, so the resolution is preserved.
            AVFilterInOut* outputs = ffmpeg.avfilter_inout_alloc();
            AVFilterInOut* inputs = ffmpeg.avfilter_inout_alloc();
            if (outputs is null || inputs is null)
            {
                if (outputs is not null) ffmpeg.avfilter_inout_free(&outputs);
                if (inputs is not null) ffmpeg.avfilter_inout_free(&inputs);
                SampleLog.Write("VAAPI filter init: avfilter_inout_alloc failed.");
                ffmpeg.avfilter_graph_free(&graph);
                return;
            }

            outputs->name = ffmpeg.av_strdup("in");
            outputs->filter_ctx = sourceCtx;
            outputs->pad_idx = 0;
            outputs->next = null;

            inputs->name = ffmpeg.av_strdup("out");
            inputs->filter_ctx = sinkCtx;
            inputs->pad_idx = 0;
            inputs->next = null;

            const string filterDesc = "scale_vaapi=format=bgra";
            rc = ffmpeg.avfilter_graph_parse_ptr(graph, filterDesc, &inputs, &outputs, null);
            ffmpeg.avfilter_inout_free(&outputs);
            ffmpeg.avfilter_inout_free(&inputs);

            if (rc < 0)
            {
                SampleLog.Write($"VAAPI filter init: graph_parse_ptr({filterDesc}) failed: {FormatError(rc)}");
                ffmpeg.avfilter_graph_free(&graph);
                return;
            }

            rc = ffmpeg.avfilter_graph_config(graph, null);
            if (rc < 0)
            {
                SampleLog.Write($"VAAPI filter init: graph_config failed: {FormatError(rc)}");
                ffmpeg.avfilter_graph_free(&graph);
                return;
            }

            _vaapiFilterGraph = graph;
            _vaapiFilterSource = sourceCtx;
            _vaapiFilterSink = sinkCtx;
            graph = null; // ownership handed off; don't free in catch
            SampleLog.Write($"VAAPI filter graph ready: {filterDesc}");
        }
        finally
        {
            if (graph is not null) ffmpeg.avfilter_graph_free(&graph);
        }
    }

    /// <summary>
    /// Push <paramref name="hwFrame"/> through the scale_vaapi filter and pull the
    /// converted BGRA frame out. Returns the filter output frame on success
    /// (caller takes ownership of the ref — pair with <c>av_frame_unref</c> after
    /// use), or <see langword="null"/> if the filter isn't initialized or any
    /// step fails.
    /// </summary>
    private AVFrame* ProcessVaapiFilter(AVFrame* hwFrame)
    {
        if (_vaapiFilterGraph is null)
        {
            // Try to init lazily — codec may have allocated hw_frames_ctx by now.
            TryInitVaapiFilterGraph();
            if (_vaapiFilterGraph is null) return null;
        }

        // AV_BUFFERSRC_FLAG_KEEP_REF (=8): the buffersrc takes its own ref on the
        // frame instead of stealing the input ref. We need this because hwFrame is
        // _frame which the decoder reuses next iteration.
        const int AvBuffersrcFlagKeepRef = 8;
        int rc = ffmpeg.av_buffersrc_add_frame_flags(_vaapiFilterSource, hwFrame, AvBuffersrcFlagKeepRef);
        if (rc < 0)
        {
            SampleLog.Write($"VAAPI filter: buffersrc_add_frame failed: {FormatError(rc)}");
            return null;
        }

        ffmpeg.av_frame_unref(_vaapiFilteredFrame);
        rc = ffmpeg.av_buffersink_get_frame(_vaapiFilterSink, _vaapiFilteredFrame);
        if (rc < 0)
        {
            // EAGAIN here means "filter needs more input" — would happen for
            // filters that buffer (delay), but scale_vaapi is 1:1 so this is rare.
            SampleLog.Write($"VAAPI filter: buffersink_get_frame failed: {FormatError(rc)}");
            return null;
        }

        return _vaapiFilteredFrame;
    }

    /// <summary>
    /// Build an <c>AVHWFramesContext</c> with <c>sw_format = AV_PIX_FMT_BGRA</c> and
    /// attach it to the codec context. VideoToolbox honors this and configures its
    /// VTDecompressionSession to output 32BGRA CVPixelBuffers (single IOSurface plane).
    /// Must be called AFTER <c>av_hwdevice_ctx_create</c> and BEFORE <c>avcodec_open2</c>.
    /// </summary>
    private bool TryConfigureBgraHwFramesContext(AVCodecContext* codecPar)
    {
        if (codecPar->hw_device_ctx is null)
        {
            return false;
        }

        AVBufferRef* framesRef = ffmpeg.av_hwframe_ctx_alloc(codecPar->hw_device_ctx);
        if (framesRef is null)
        {
            return false;
        }

        try
        {
            var framesCtx = (AVHWFramesContext*)framesRef->data;
            framesCtx->format = AVPixelFormat.AV_PIX_FMT_VIDEOTOOLBOX;
            framesCtx->sw_format = AVPixelFormat.AV_PIX_FMT_BGRA;
            framesCtx->width = codecPar->width;
            framesCtx->height = codecPar->height;
            framesCtx->initial_pool_size = 0;   // VT manages its own pool

            int initResult = ffmpeg.av_hwframe_ctx_init(framesRef);
            if (initResult < 0)
            {
                SampleLog.Write($"av_hwframe_ctx_init for BGRA failed: {FormatError(initResult)}");
                return false;
            }

            codecPar->hw_frames_ctx = ffmpeg.av_buffer_ref(framesRef);
            return codecPar->hw_frames_ctx is not null;
        }
        finally
        {
            ffmpeg.av_buffer_unref(&framesRef);
        }
    }

    /// <summary>
    /// Builds a D3D11 device with both BGRA_SUPPORT and VIDEO_SUPPORT, wraps it in an
    /// FFmpeg AVHWDeviceContext, and assigns it as the codec's hw_device_ctx. Returns
    /// false on any failure — caller falls back to FFmpeg's built-in device creation.
    /// </summary>
    private bool TryCreateInteropReadyD3D11Device()
    {
        if (_preferredD3D11Device != 0)
        {
            SampleLog.Write("TryCreateInteropReadyD3D11Device: trying factory-owned D3D11 device.");
            if (TryCreateHwDeviceContextFromExistingD3D11Device(_preferredD3D11Device))
            {
                return true;
            }

            SampleLog.Write("Factory-owned D3D11 device was not accepted by FFmpeg. Falling back to sample-owned device creation.");
        }

        SampleLog.Write("TryCreateBgraD3D11Device: calling D3D11CreateDevice with BGRA+VIDEO ...");
        nint d3dDevice;
        nint d3dCtx;
        int hr = D3D11Native.D3D11CreateDevice(
            adapter: 0,
            driverType: D3D11Native.D3D_DRIVER_TYPE_HARDWARE,
            software: 0,
            flags: D3D11Native.D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11Native.D3D11_CREATE_DEVICE_VIDEO_SUPPORT,
            pFeatureLevels: 0,
            featureLevels: 0,
            sdkVersion: D3D11Native.D3D11_SDK_VERSION,
            ppDevice: out d3dDevice,
            pFeatureLevel: out _,
            ppImmediateContext: out d3dCtx);
        if (hr < 0 || d3dDevice == 0)
        {
            SampleLog.Write($"D3D11CreateDevice (BGRA+VIDEO) failed: 0x{hr:X8}.");
            if (d3dCtx != 0) Marshal.Release(d3dCtx);
            return false;
        }
        if (d3dCtx != 0 && D3D11Native.TryEnableMultithreadProtection(d3dCtx))
        {
            SampleLog.Write("Enabled ID3D11Multithread protection on the BGRA+VIDEO device context.");
        }
        // We don't use the immediate context ourselves — FFmpeg will GetImmediateContext
        // on the device during av_hwdevice_ctx_init, which AddRef's its own copy.
        if (d3dCtx != 0) Marshal.Release(d3dCtx);

        AVBufferRef* deviceRef = ffmpeg.av_hwdevice_ctx_alloc(AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA);
        if (deviceRef is null)
        {
            Marshal.Release(d3dDevice);
            return false;
        }

        return TryInitializeHwDeviceContext(deviceRef, d3dDevice, ownsDeviceReference: true);
    }

    private bool TryCreateHwDeviceContextFromExistingD3D11Device(nint d3dDevice)
    {
        if (d3dDevice == 0)
        {
            return false;
        }

        AVBufferRef* deviceRef = ffmpeg.av_hwdevice_ctx_alloc(AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA);
        if (deviceRef is null)
        {
            return false;
        }

        Marshal.AddRef(d3dDevice);
        return TryInitializeHwDeviceContext(deviceRef, d3dDevice, ownsDeviceReference: true);
    }

    private bool TryInitializeHwDeviceContext(AVBufferRef* deviceRef, nint d3dDevice, bool ownsDeviceReference)
    {
        if (deviceRef is null || d3dDevice == 0)
        {
            return false;
        }

        // First field of AVD3D11VADeviceContext is `ID3D11Device *device`. FFmpeg takes
        // ownership of the device pointer (its free callback will Release it), so we
        // hand over our single ref without an additional AddRef.
        var hwDeviceCtx = (AVHWDeviceContext*)deviceRef->data;
        *(nint*)hwDeviceCtx->hwctx = d3dDevice;

        int initResult = ffmpeg.av_hwdevice_ctx_init(deviceRef);
        if (initResult < 0)
        {
            SampleLog.Write($"av_hwdevice_ctx_init failed: {FormatError(initResult)}.");
            ffmpeg.av_buffer_unref(&deviceRef);
            return false;
        }

        // Hand a reference to the codec; deviceRef itself is released after.
        _codec->hw_device_ctx = ffmpeg.av_buffer_ref(deviceRef);
        ffmpeg.av_buffer_unref(&deviceRef);
        bool ok = _codec->hw_device_ctx is not null;
        SampleLog.Write($"TryCreateBgraD3D11Device: success={ok}");
        return ok;
    }

    private void LogHardwareDecodeSetup(AVCodec* codec)
    {
        List<string> hardwareConfigs = [];
        for (int configIndex = 0; ; configIndex++)
        {
            var config = ffmpeg.avcodec_get_hw_config(codec, configIndex);
            if (config is null)
            {
                break;
            }

            string deviceTypeName = ffmpeg.av_hwdevice_get_type_name(config->device_type) ?? "unknown";
            hardwareConfigs.Add($"{deviceTypeName} pix_fmt={config->pix_fmt} methods=0x{config->methods:X}");
        }

        if (hardwareConfigs.Count == 0)
        {
            SampleLog.Write($"HW decode support: codec exposes no FFmpeg hw configs. active=false, codecPixFmt={_codec->pix_fmt}");
            return;
        }

        SampleLog.Write($"HW decode configs exposed by codec: {string.Join("; ", hardwareConfigs)}");
        string deviceLabel = _activeHardwareDeviceType switch
        {
            AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX => "videotoolbox",
            AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI => "vaapi",
            AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA => "cuda/nvdec",
            AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA => "d3d11va",
            _ => "unknown",
        };
        SampleLog.Write($"HW decode active at open: {_hardwareDecodeEnabled} ({(_hardwareDecodeEnabled ? $"device={deviceLabel}, hw_pix_fmt={_hardwarePixelFormat}" : "no hw_device_ctx configured")}). codecPixFmt={_codec->pix_fmt}");
    }

    private void LogActiveDecodePath(AVFrame* frame)
    {
        if (_hardwareDecodeLogged)
        {
            return;
        }

        _hardwareDecodeLogged = true;
        bool hasHardwareFramesContext = frame->hw_frames_ctx is not null;
        bool hasHardwareDeviceContext = _codec->hw_device_ctx is not null;
        SampleLog.Write(
            $"Decode path in use: {(hasHardwareFramesContext || hasHardwareDeviceContext ? "hardware-backed" : "software")}, frameFormat={(AVPixelFormat)frame->format}, codecPixFmt={_codec->pix_fmt}, hw_frames_ctx={(hasHardwareFramesContext ? "set" : "null")}, hw_device_ctx={(hasHardwareDeviceContext ? "set" : "null")}");
    }

    private AVCodecHWConfig* FindHardwareConfig(AVCodec* codec, AVHWDeviceType deviceType)
    {
        for (int configIndex = 0; ; configIndex++)
        {
            var config = ffmpeg.avcodec_get_hw_config(codec, configIndex);
            if (config is null)
            {
                return null;
            }

            if (config->device_type == deviceType)
            {
                return config;
            }
        }
    }

    private AVFrame* GetFrameForColorConversion(AVFrame* decodedFrame)
    {
        bool isHardwareFrame = decodedFrame->hw_frames_ctx is not null || (_hardwareDecodeEnabled && (AVPixelFormat)decodedFrame->format == _hardwarePixelFormat);
        if (!isHardwareFrame)
        {
            return decodedFrame;
        }

        ffmpeg.av_frame_unref(_softwareFrame);
        ThrowIfError(ffmpeg.av_hwframe_transfer_data(_softwareFrame, decodedFrame, 0), "transfer hardware frame to software frame");
        ThrowIfError(ffmpeg.av_frame_copy_props(_softwareFrame, decodedFrame), "copy hardware frame properties");
        return _softwareFrame;
    }

    private void CaptureHardwareFrameMetadata(AVFrame* decodedFrame, out nint d3d11TextureHandle, out int d3d11SubresourceIndex, out bool hardwareDecoded)
    {
        d3d11TextureHandle = 0;
        d3d11SubresourceIndex = 0;
        hardwareDecoded = false;    

        bool isHardwareFrame = decodedFrame->hw_frames_ctx is not null || (_hardwareDecodeEnabled && (AVPixelFormat)decodedFrame->format == _hardwarePixelFormat);
        if (!isHardwareFrame)
        {
            return;
        }

        nint textureHandle = (nint)decodedFrame->data[0];
        if (textureHandle == 0)
        {
            return;
        }

        Marshal.AddRef(textureHandle);
        d3d11TextureHandle = textureHandle;
        d3d11SubresourceIndex = unchecked((int)(nint)decodedFrame->data[1]);
        hardwareDecoded = true;
    }

    private void ConvertCurrentFrame(AVFrame* sourceFrame, Span<byte> bgraBuffer)
    {
        _sws = ffmpeg.sws_getCachedContext(
            _sws,
            sourceFrame->width,
            sourceFrame->height,
            (AVPixelFormat)sourceFrame->format,
            Width,
            Height,
            AVPixelFormat.AV_PIX_FMT_BGRA,
            (int)SwsFlags.SWS_BILINEAR,
            null,
            null,
            null);

        if (_sws is null)
        {
            throw new InvalidOperationException($"Failed to create FFmpeg swscale context for {(AVPixelFormat)sourceFrame->format}.");
        }

        fixed (byte* destination = &MemoryMarshal.GetReference(bgraBuffer))
        {
            byte_ptrArray4 dstData = default;
            int_array4 dstLinesize = default;

            ThrowIfError(
                ffmpeg.av_image_fill_arrays(ref dstData, ref dstLinesize, destination, AVPixelFormat.AV_PIX_FMT_BGRA, Width, Height, 1),
                "prepare BGRA output");

            int scaled = ffmpeg.sws_scale(_sws, sourceFrame->data, sourceFrame->linesize, 0, sourceFrame->height, dstData, dstLinesize);
            if (scaled <= 0)
            {
                throw new InvalidOperationException("FFmpeg failed to convert the current frame to BGRA.");
            }
        }
    }

    private static TimeSpan GetDuration(AVStream* stream, AVFormatContext* format)
    {
        if (stream->duration > 0)
        {
            return TimestampToTimeSpan(stream->duration, stream->time_base);
        }

        if (format->duration > 0)
        {
            return TimeSpan.FromSeconds(format->duration / (double)ffmpeg.AV_TIME_BASE);
        }

        return TimeSpan.Zero;
    }

    private static TimeSpan TimestampToTimeSpan(long timestamp, AVRational timeBase)
    {
        if (timestamp == ffmpeg.AV_NOPTS_VALUE || timeBase.den == 0)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromSeconds(timestamp * ffmpeg.av_q2d(timeBase));
    }

    private static long TimeSpanToTimestamp(TimeSpan time, AVRational timeBase)
    {
        if (timeBase.num == 0)
        {
            return 0;
        }

        double seconds = time.TotalSeconds;
        return (long)Math.Round(seconds / ffmpeg.av_q2d(timeBase));
    }

    private static void ThrowIfError(int errorCode, string action)
    {
        if (errorCode >= 0)
        {
            return;
        }

        Span<byte> buffer = stackalloc byte[1024];
        fixed (byte* pBuffer = buffer)
        {
            ffmpeg.av_strerror(errorCode, pBuffer, (ulong)buffer.Length);
            string message = Marshal.PtrToStringAnsi((nint)pBuffer) ?? $"FFmpeg error {errorCode}";
            throw new InvalidOperationException($"Failed to {action}: {message}");
        }
    }

    private static string FormatError(int errorCode)
    {
        if (errorCode >= 0)
        {
            return "success";
        }

        Span<byte> buffer = stackalloc byte[1024];
        fixed (byte* pBuffer = buffer)
        {
            ffmpeg.av_strerror(errorCode, pBuffer, (ulong)buffer.Length);
            return Marshal.PtrToStringAnsi((nint)pBuffer) ?? $"FFmpeg error {errorCode}";
        }
    }

    private static double RationalToDouble(AVRational value)
    {
        if (value.den == 0)
        {
            return 0;
        }

        return ffmpeg.av_q2d(value);
    }

    private static string FormatBitRate(long bitRate)
    {
        if (bitRate >= 1_000_000)
        {
            return $"{bitRate / 1_000_000d:0.##} Mbps";
        }

        if (bitRate >= 1_000)
        {
            return $"{bitRate / 1_000d:0.##} kbps";
        }

        return $"{bitRate} bps";
    }

    private static string FormatDouble(double value)
    {
        return value > 0 ? value.ToString("0.###") : "n/a";
    }

    private static string FormatRational(AVRational value)
    {
        if (value.den == 0)
        {
            return "n/a";
        }

        return $"{value.num}/{value.den}";
    }
}
