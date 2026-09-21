using System.Collections.Generic;
using System.Runtime.InteropServices;

using Aprillz.MewUI.Video.Sample.Diagnostics;

namespace Aprillz.MewUI.Video.Sample.Decoding;

internal sealed unsafe class D3D11VideoProcessorConverter : IDisposable
{
    private const int OutputTexturePoolSize = 6;
    private const int QueryInterfaceIndex = 0;
    private const int AddRefIndex = 1;
    private const int ReleaseIndex = 2;

    private const int DeviceCreateTexture2DIndex = 5;
    private const int DeviceCreateQueryIndex = 24;
    private const int ContextEndIndex = 28;
    private const int ContextGetDataIndex = 29;
    private const int DxgiResource1CreateSharedHandleIndex = 13;
    private const int DeviceGetImmediateContextIndex = 40;

    private const int VideoDeviceCreateVideoProcessorIndex = 4;
    private const int VideoDeviceCreateVideoProcessorInputViewIndex = 8;
    private const int VideoDeviceCreateVideoProcessorOutputViewIndex = 9;
    private const int VideoDeviceCreateVideoProcessorEnumeratorIndex = 10;

    private const int EnumeratorCheckVideoProcessorFormatIndex = 8;
    private const int EnumeratorGetVideoProcessorCapsIndex = 9;

    private const int VideoContextSetStreamFrameFormatIndex = 27;
    private const int VideoContextSetStreamSourceRectIndex = 30;
    private const int VideoContextVideoProcessorBltIndex = 53;

    private const uint D3D11_USAGE_DEFAULT = 0;
    private const uint D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE = 0;
    private const uint D3D11_VIDEO_USAGE_PLAYBACK_NORMAL = 0;
    private const uint D3D11_VPIV_DIMENSION_TEXTURE2D = 1;
    private const uint D3D11_VPOV_DIMENSION_TEXTURE2D = 1;
    private const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;
    private const uint DXGI_FORMAT_R8G8B8A8_UNORM = 28;
    private const uint D3D11_RESOURCE_MISC_SHARED = 0x2;
    private const uint D3D11_RESOURCE_MISC_SHARED_NTHANDLE = 0x800;
    private const uint DXGI_SHARED_RESOURCE_READ = 0x80000000;
    private const uint DXGI_SHARED_RESOURCE_WRITE = 0x1;
    private const uint D3D11_QUERY_EVENT = 0;

    private static readonly Guid IID_ID3D11VideoDevice = new("10EC4D5B-975A-4689-B9E4-D0AAC30FE333");
    private static readonly Guid IID_ID3D11VideoContext = new("61F21C45-3C0E-4A74-9CEA-67100D9AD5E4");
    private static readonly Guid IID_IDXGIResource1 = new("30961379-4609-4A41-998E-54FE567EE0C1");

    private readonly nint _device;
    private readonly nint _deviceContext;
    private readonly nint _videoDevice;
    private readonly nint _videoContext;
    private readonly Queue<nint> _availableOutputTextures = new();
    // Diagnostic keys already written, so a failure that repeats every frame is logged once.
    private readonly HashSet<string> _loggedDiagnostics = new();
    private readonly object _outputTextureGate = new();
    // Shared output: NT handle and last producer fence value per output texture, for GL memory-object import.
    private readonly Dictionary<nint, nint> _sharedHandles = new();
    private readonly Dictionary<nint, ulong> _producedValues = new();
    private readonly bool _sharedOutput;
    private readonly SharedTextureFences? _fences;
    // Fallback when shared fences are unavailable: a CPU wait on this event query after each conversion.
    private nint _completionQuery;

    private nint _processorEnumerator;
    private nint _videoProcessor;
    private uint _inputFormat;
    private uint _inputWidth;
    private uint _inputHeight;
    private uint _outputWidth;
    private uint _outputHeight;
    private bool _disposed;

    private D3D11VideoProcessorConverter(nint device, nint deviceContext, nint videoDevice, nint videoContext, bool sharedOutput)
    {
        _device = device;
        _deviceContext = deviceContext;
        _videoDevice = videoDevice;
        _videoContext = videoContext;
        _sharedOutput = sharedOutput;
        _fences = sharedOutput ? SharedTextureFences.TryCreate(device, deviceContext) : null;
    }

    /// <summary>
    /// True when output textures are RGBA and NT-handle shared for import into another graphics API.
    /// </summary>
    public bool SharedOutput => _sharedOutput;

    /// <summary>
    /// Fences ordering shared-output writes against external reads, or null when unavailable (conversions then complete before returning).
    /// </summary>
    public SharedTextureFences? Fences => _fences;

    /// <summary>
    /// Returns the producer fence value signaled after the latest conversion into <paramref name="outputTexture"/>, or 0.
    /// </summary>
    public ulong GetProducedValue(nint outputTexture)
    {
        lock (_outputTextureGate)
        {
            return _producedValues.TryGetValue(outputTexture, out ulong value) ? value : 0;
        }
    }

    /// <summary>
    /// Returns the NT shared handle of an output texture created in shared mode, or 0.
    /// </summary>
    public nint GetSharedHandle(nint outputTexture)
    {
        lock (_outputTextureGate)
        {
            return _sharedHandles.TryGetValue(outputTexture, out nint handle) ? handle : 0;
        }
    }

    public static bool TryCreate(nint device, out D3D11VideoProcessorConverter? converter, bool sharedOutput = false)
    {
        converter = null;
        if (device == 0)
        {
            return false;
        }

        AddRef(device);

        nint deviceContext = 0;
        nint videoDevice = 0;
        nint videoContext = 0;

        try
        {
            SampleLog.Write($"[vp-diag] converter device adapter: {D3D11Native.DescribeAdapter(device)}");

            GetImmediateContext(device, out deviceContext);
            if (deviceContext == 0)
            {
                SampleLog.Write("[vp-diag] TryCreate: GetImmediateContext returned null.");
                return false;
            }

            int videoDeviceResult = QueryInterface(device, IID_ID3D11VideoDevice, out videoDevice);
            if (videoDeviceResult < 0 || videoDevice == 0)
            {
                SampleLog.Write($"[vp-diag] TryCreate: QueryInterface(ID3D11VideoDevice) failed hr=0x{videoDeviceResult:X8}.");
                return false;
            }

            int videoContextResult = QueryInterface(deviceContext, IID_ID3D11VideoContext, out videoContext);
            if (videoContextResult < 0 || videoContext == 0)
            {
                SampleLog.Write($"[vp-diag] TryCreate: QueryInterface(ID3D11VideoContext) failed hr=0x{videoContextResult:X8}.");
                return false;
            }

            converter = new D3D11VideoProcessorConverter(device, deviceContext, videoDevice, videoContext, sharedOutput);
            return true;
        }
        catch (Exception ex)
        {
            SampleLog.Write($"[vp-diag] TryCreate threw: {ex.Message}");
            return false;
        }
        finally
        {
            if (converter is null)
            {
                ReleaseIfNeeded(videoContext);
                ReleaseIfNeeded(videoDevice);
                ReleaseIfNeeded(deviceContext);
                ReleaseIfNeeded(device);
            }
        }
    }

    public bool TryConvert(nint inputTexture, int arraySlice, int outputWidth, int outputHeight, out nint outputTexture)
    {
        outputTexture = 0;
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!D3D11Native.TryGetTexture2DDesc(inputTexture, out var inputDesc))
        {
            LogOnce("input-desc", "TryConvert: GetDesc on the decoder texture failed.");
            return false;
        }

        LogOnce("input-texture", $"TryConvert input: texture=0x{inputTexture:X} slice={arraySlice} {D3D11Native.DescribeTexture(inputTexture)} output={outputWidth}x{outputHeight}");

        if (!EnsureVideoProcessor(inputDesc.Format, inputDesc.Width, inputDesc.Height, (uint)outputWidth, (uint)outputHeight))
        {
            return false;
        }

        if (!TryRentOutputTexture((uint)outputWidth, (uint)outputHeight, out outputTexture))
        {
            return false;
        }

        nint inputView = 0;
        nint outputView = 0;
        bool success = false;

        try
        {
            if (!TryCreateInputView(inputTexture, arraySlice, inputDesc.Format, out inputView)
                || !TryCreateOutputView(outputTexture, out outputView))
            {
                return false;
            }

            _fences?.WaitBeforeWrite(outputTexture);
            SetStreamFrameFormat(_videoContext, _videoProcessor, 0, D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE);
            // Decoder surfaces are padded past the picture (2160 rows coded into 2176); without a source rect the padding is scaled in as a green band.
            SetStreamSourceRect(_videoContext, _videoProcessor, 0, new RECT { Left = 0, Top = 0, Right = outputWidth, Bottom = outputHeight });

            D3D11_VIDEO_PROCESSOR_STREAM stream = new()
            {
                Enable = 1,
                OutputIndex = 0,
                InputFrameOrField = 0,
                PastFrames = 0,
                FutureFrames = 0,
                ppPastSurfaces = 0,
                pInputSurface = inputView,
                ppFutureSurfaces = 0,
                ppPastSurfacesRight = 0,
                pInputSurfaceRight = 0,
                ppFutureSurfacesRight = 0,
            };

            int bltResult = VideoProcessorBlt(_videoContext, _videoProcessor, outputView, 0, 1, &stream);
            if (bltResult < 0)
            {
                LogOnce("blt", $"VideoProcessorBlt failed hr=0x{bltResult:X8} slice={arraySlice}.");
                return false;
            }

            if (_fences is not null)
            {
                ulong producedValue = _fences.SignalProduced();
                lock (_outputTextureGate)
                {
                    _producedValues[outputTexture] = producedValue;
                }
            }

            D3D11Native.FlushDeviceContext(_deviceContext);
            if (_sharedOutput && (_fences is null || _fences.CpuCompletionRequired))
            {
                WaitForGpuCompletion();
            }

            LogOnce("blt-ok", $"VideoProcessorBlt succeeded: output=0x{outputTexture:X} {D3D11Native.DescribeTexture(outputTexture)}");
            success = true;
            return true;
        }
        finally
        {
            ReleaseIfNeeded(outputView);
            ReleaseIfNeeded(inputView);

            if (!success && outputTexture != 0)
            {
                ReturnOutputTexture(outputTexture);
                outputTexture = 0;
            }
        }
    }

    public void ReturnOutputTexture(nint outputTexture)
    {
        if (outputTexture == 0)
        {
            return;
        }

        lock (_outputTextureGate)
        {
            if (_disposed)
            {
                ReleaseOutputTextureLocked(outputTexture);
                return;
            }

            if (_availableOutputTextures.Count >= OutputTexturePoolSize)
            {
                ReleaseOutputTextureLocked(outputTexture);
                return;
            }

            _availableOutputTextures.Enqueue(outputTexture);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseProcessorResources();
        ReleaseIfNeeded(_completionQuery);
        _completionQuery = 0;
        _fences?.Dispose();
        ReleaseIfNeeded(_videoContext);
        ReleaseIfNeeded(_videoDevice);
        ReleaseIfNeeded(_deviceContext);
        ReleaseIfNeeded(_device);
    }

    private bool EnsureVideoProcessor(uint inputFormat, uint inputWidth, uint inputHeight, uint outputWidth, uint outputHeight)
    {
        if (_videoProcessor != 0
            && _processorEnumerator != 0
            && _inputFormat == inputFormat
            && _inputWidth == inputWidth
            && _inputHeight == inputHeight
            && _outputWidth == outputWidth
            && _outputHeight == outputHeight)
        {
            return true;
        }

        ReleaseProcessorResources();

        DXGI_RATIONAL frameRate = new() { Numerator = 30, Denominator = 1 };
        D3D11_VIDEO_PROCESSOR_CONTENT_DESC desc = new()
        {
            InputFrameFormat = D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE,
            InputFrameRate = frameRate,
            InputWidth = inputWidth,
            InputHeight = inputHeight,
            OutputFrameRate = frameRate,
            OutputWidth = outputWidth,
            OutputHeight = outputHeight,
            Usage = D3D11_VIDEO_USAGE_PLAYBACK_NORMAL,
        };

        string contentText = $"input={inputWidth}x{inputHeight} format={inputFormat} output={outputWidth}x{outputHeight}";
        int enumeratorResult = CreateVideoProcessorEnumerator(_videoDevice, &desc, out _processorEnumerator);
        if (enumeratorResult < 0 || _processorEnumerator == 0)
        {
            LogOnce("enumerator", $"CreateVideoProcessorEnumerator failed hr=0x{enumeratorResult:X8} ({contentText}).");
            _processorEnumerator = 0;
            return false;
        }

        LogProcessorSupport(inputFormat, contentText);

        int processorResult = CreateVideoProcessor(_videoDevice, _processorEnumerator, 0, out _videoProcessor);
        if (processorResult < 0 || _videoProcessor == 0)
        {
            LogOnce("processor", $"CreateVideoProcessor failed hr=0x{processorResult:X8} ({contentText}).");
            ReleaseProcessorResources();
            return false;
        }

        _inputFormat = inputFormat;
        _inputWidth = inputWidth;
        _inputHeight = inputHeight;
        _outputWidth = outputWidth;
        _outputHeight = outputHeight;
        return true;
    }

    private bool TryRentOutputTexture(uint width, uint height, out nint outputTexture)
    {
        lock (_outputTextureGate)
        {
            while (_availableOutputTextures.Count > 0)
            {
                nint pooledTexture = _availableOutputTextures.Dequeue();
                if (pooledTexture == 0)
                {
                    continue;
                }

                outputTexture = pooledTexture;
                return true;
            }
        }

        D3D11Native.D3D11_TEXTURE2D_DESC desc = new()
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = _sharedOutput ? DXGI_FORMAT_R8G8B8A8_UNORM : DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDescCount = 1,
            SampleDescQuality = 0,
            Usage = D3D11_USAGE_DEFAULT,
            BindFlags = D3D11Native.D3D11_BIND_SHADER_RESOURCE | D3D11Native.D3D11_BIND_RENDER_TARGET,
            CPUAccessFlags = 0,
            MiscFlags = _sharedOutput ? D3D11_RESOURCE_MISC_SHARED | D3D11_RESOURCE_MISC_SHARED_NTHANDLE : 0,
        };

        outputTexture = 0;
        int createResult = CreateTexture2D(_device, &desc, 0, out outputTexture);
        if (createResult < 0 || outputTexture == 0)
        {
            LogOnce("output-texture", $"CreateTexture2D (output {width}x{height} shared={_sharedOutput}) failed hr=0x{createResult:X8}.");
            outputTexture = 0;
            return false;
        }

        if (_sharedOutput)
        {
            int handleResult = CreateSharedHandle(outputTexture, out nint sharedHandle);
            if (handleResult < 0 || sharedHandle == 0)
            {
                LogOnce("shared-handle", $"IDXGIResource1::CreateSharedHandle failed hr=0x{handleResult:X8}.");
                ReleaseIfNeeded(outputTexture);
                outputTexture = 0;
                return false;
            }

            lock (_outputTextureGate)
            {
                _sharedHandles[outputTexture] = sharedHandle;
            }

            LogOnce("shared-handle-ok", $"shared output texture created: 0x{outputTexture:X} handle=0x{sharedHandle:X} {D3D11Native.DescribeTexture(outputTexture)}");
        }

        return true;
    }

    private void ReleaseOutputTextureLocked(nint outputTexture)
    {
        if (_sharedHandles.Remove(outputTexture, out nint sharedHandle))
        {
            CloseHandle(sharedHandle);
        }

        _producedValues.Remove(outputTexture);
        _fences?.Forget(outputTexture);

        ReleaseIfNeeded(outputTexture);
    }

    private void WaitForGpuCompletion()
    {
        if (_completionQuery == 0)
        {
            D3D11_QUERY_DESC queryDesc = new() { Query = D3D11_QUERY_EVENT, MiscFlags = 0 };
            var createQuery = (delegate* unmanaged[Stdcall]<nint, D3D11_QUERY_DESC*, nint*, int>)(*(nint**)_device)[DeviceCreateQueryIndex];
            nint query = 0;
            int queryResult = createQuery(_device, &queryDesc, &query);
            if (queryResult < 0 || query == 0)
            {
                LogOnce("query", $"CreateQuery(EVENT) failed hr=0x{queryResult:X8}.");
                return;
            }

            _completionQuery = query;
        }

        var contextVtable = *(nint**)_deviceContext;
        var end = (delegate* unmanaged[Stdcall]<nint, nint, void>)contextVtable[ContextEndIndex];
        var getData = (delegate* unmanaged[Stdcall]<nint, nint, void*, uint, uint, int>)contextVtable[ContextGetDataIndex];
        end(_deviceContext, _completionQuery);

        int completed = 0;
        while (getData(_deviceContext, _completionQuery, &completed, sizeof(int), 0) == 1)
        {
            Thread.Yield();
        }
    }

    private static int CreateSharedHandle(nint texture, out nint sharedHandle)
    {
        sharedHandle = 0;
        int queryResult = QueryInterface(texture, IID_IDXGIResource1, out nint dxgiResource);
        if (queryResult < 0 || dxgiResource == 0)
        {
            return queryResult;
        }

        try
        {
            var createHandle = (delegate* unmanaged[Stdcall]<nint, nint, uint, char*, nint*, int>)(*(nint**)dxgiResource)[DxgiResource1CreateSharedHandleIndex];
            nint localHandle = 0;
            int result = createHandle(dxgiResource, 0, DXGI_SHARED_RESOURCE_READ | DXGI_SHARED_RESOURCE_WRITE, null, &localHandle);
            sharedHandle = localHandle;
            return result;
        }
        finally
        {
            ReleaseIfNeeded(dxgiResource);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);

    private bool TryCreateInputView(nint inputTexture, int arraySlice, uint format, out nint inputView)
    {
        D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC desc = new()
        {
            // FourCC is only for non-DXGI surfaces; 0 takes the resource's format. Intel rejects a DXGI value here.
            FourCC = 0,
            ViewDimension = D3D11_VPIV_DIMENSION_TEXTURE2D,
            Texture2D = new D3D11_TEX2D_VPIV
            {
                MipSlice = 0,
                ArraySlice = unchecked((uint)arraySlice),
            },
        };

        inputView = 0;
        int createResult = CreateVideoProcessorInputView(_videoDevice, inputTexture, _processorEnumerator, &desc, out inputView);
        if (createResult >= 0 && inputView != 0)
        {
            return true;
        }

        LogOnce("input-view", $"CreateVideoProcessorInputView failed hr=0x{createResult:X8} format={format} slice={arraySlice}.");
        inputView = 0;
        return false;
    }

    private bool TryCreateOutputView(nint outputTexture, out nint outputView)
    {
        D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC desc = new()
        {
            ViewDimension = D3D11_VPOV_DIMENSION_TEXTURE2D,
            Texture2D = new D3D11_TEX2D_VPOV
            {
                MipSlice = 0,
            },
        };

        outputView = 0;
        int createResult = CreateVideoProcessorOutputView(_videoDevice, outputTexture, _processorEnumerator, &desc, out outputView);
        if (createResult < 0 || outputView == 0)
        {
            LogOnce("output-view", $"CreateVideoProcessorOutputView failed hr=0x{createResult:X8}.");
            outputView = 0;
            return false;
        }

        return true;
    }

    private void LogOnce(string key, string message)
    {
        if (_loggedDiagnostics.Add(key))
        {
            SampleLog.Write($"[vp-diag] {message}");
        }
    }

    private void LogProcessorSupport(uint inputFormat, string contentText)
    {
        uint inputSupport = 0;
        uint outputSupport = 0;
        int inputResult = CheckVideoProcessorFormat(_processorEnumerator, inputFormat, &inputSupport);
        int outputResult = CheckVideoProcessorFormat(_processorEnumerator, _sharedOutput ? DXGI_FORMAT_R8G8B8A8_UNORM : DXGI_FORMAT_B8G8R8A8_UNORM, &outputSupport);

        D3D11_VIDEO_PROCESSOR_CAPS caps = default;
        int capsResult = GetVideoProcessorCaps(_processorEnumerator, &caps);

        // Support flags: 0x1 = usable as input, 0x2 = usable as output.
        LogOnce(
            $"support-{contentText}",
            $"processor ({contentText}): input format support=0x{inputSupport:X} hr=0x{inputResult:X8}, BGRA support=0x{outputSupport:X} hr=0x{outputResult:X8}, caps hr=0x{capsResult:X8} device=0x{caps.DeviceCaps:X} feature=0x{caps.FeatureCaps:X} inputFormatCaps=0x{caps.InputFormatCaps:X} maxInputStreams={caps.MaxInputStreams}");
    }

    private void ReleaseProcessorResources()
    {
        ClearOutputTexturePool();
        ReleaseIfNeeded(_videoProcessor);
        ReleaseIfNeeded(_processorEnumerator);
        _videoProcessor = 0;
        _processorEnumerator = 0;
        _inputFormat = 0;
        _inputWidth = 0;
        _inputHeight = 0;
        _outputWidth = 0;
        _outputHeight = 0;
    }

    private void ClearOutputTexturePool()
    {
        lock (_outputTextureGate)
        {
            while (_availableOutputTextures.Count > 0)
            {
                ReleaseOutputTextureLocked(_availableOutputTextures.Dequeue());
            }
        }
    }

    private static void AddRef(nint unknown)
    {
        var vtbl = *(nint**)unknown;
        var addRef = (delegate* unmanaged[Stdcall]<nint, uint>)vtbl[AddRefIndex];
        _ = addRef(unknown);
    }

    private static int QueryInterface(nint unknown, Guid iid, out nint result)
    {
        var vtbl = *(nint**)unknown;
        var queryInterface = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)vtbl[QueryInterfaceIndex];
        nint localResult = 0;
        int hr = queryInterface(unknown, &iid, &localResult);
        result = localResult;
        return hr;
    }

    private static void GetImmediateContext(nint device, out nint deviceContext)
    {
        var vtbl = *(nint**)device;
        var getImmediateContext = (delegate* unmanaged[Stdcall]<nint, nint*, void>)vtbl[DeviceGetImmediateContextIndex];
        nint localDeviceContext = 0;
        getImmediateContext(device, &localDeviceContext);
        deviceContext = localDeviceContext;
    }

    private static int CreateTexture2D(nint device, D3D11Native.D3D11_TEXTURE2D_DESC* desc, nint initialData, out nint texture)
    {
        var vtbl = *(nint**)device;
        var createTexture2D = (delegate* unmanaged[Stdcall]<nint, D3D11Native.D3D11_TEXTURE2D_DESC*, nint, nint*, int>)vtbl[DeviceCreateTexture2DIndex];
        nint localTexture = 0;
        int hr = createTexture2D(device, desc, initialData, &localTexture);
        texture = localTexture;
        return hr;
    }

    private static int CreateVideoProcessorEnumerator(nint videoDevice, D3D11_VIDEO_PROCESSOR_CONTENT_DESC* desc, out nint enumerator)
    {
        var vtbl = *(nint**)videoDevice;
        var createEnumerator = (delegate* unmanaged[Stdcall]<nint, D3D11_VIDEO_PROCESSOR_CONTENT_DESC*, nint*, int>)vtbl[VideoDeviceCreateVideoProcessorEnumeratorIndex];
        nint localEnumerator = 0;
        int hr = createEnumerator(videoDevice, desc, &localEnumerator);
        enumerator = localEnumerator;
        return hr;
    }

    private static int CreateVideoProcessor(nint videoDevice, nint enumerator, uint rateConversionIndex, out nint processor)
    {
        var vtbl = *(nint**)videoDevice;
        var createProcessor = (delegate* unmanaged[Stdcall]<nint, nint, uint, nint*, int>)vtbl[VideoDeviceCreateVideoProcessorIndex];
        nint localProcessor = 0;
        int hr = createProcessor(videoDevice, enumerator, rateConversionIndex, &localProcessor);
        processor = localProcessor;
        return hr;
    }

    private static int CreateVideoProcessorInputView(nint videoDevice, nint resource, nint enumerator, D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC* desc, out nint inputView)
    {
        var vtbl = *(nint**)videoDevice;
        var createInputView = (delegate* unmanaged[Stdcall]<nint, nint, nint, D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC*, nint*, int>)vtbl[VideoDeviceCreateVideoProcessorInputViewIndex];
        nint localInputView = 0;
        int hr = createInputView(videoDevice, resource, enumerator, desc, &localInputView);
        inputView = localInputView;
        return hr;
    }

    private static int CreateVideoProcessorOutputView(nint videoDevice, nint resource, nint enumerator, D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC* desc, out nint outputView)
    {
        var vtbl = *(nint**)videoDevice;
        var createOutputView = (delegate* unmanaged[Stdcall]<nint, nint, nint, D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC*, nint*, int>)vtbl[VideoDeviceCreateVideoProcessorOutputViewIndex];
        nint localOutputView = 0;
        int hr = createOutputView(videoDevice, resource, enumerator, desc, &localOutputView);
        outputView = localOutputView;
        return hr;
    }

    private static int CheckVideoProcessorFormat(nint enumerator, uint format, uint* support)
    {
        var vtbl = *(nint**)enumerator;
        var checkFormat = (delegate* unmanaged[Stdcall]<nint, uint, uint*, int>)vtbl[EnumeratorCheckVideoProcessorFormatIndex];
        return checkFormat(enumerator, format, support);
    }

    private static int GetVideoProcessorCaps(nint enumerator, D3D11_VIDEO_PROCESSOR_CAPS* caps)
    {
        var vtbl = *(nint**)enumerator;
        var getCaps = (delegate* unmanaged[Stdcall]<nint, D3D11_VIDEO_PROCESSOR_CAPS*, int>)vtbl[EnumeratorGetVideoProcessorCapsIndex];
        return getCaps(enumerator, caps);
    }

    private static void SetStreamFrameFormat(nint videoContext, nint processor, uint streamIndex, uint frameFormat)
    {
        var vtbl = *(nint**)videoContext;
        var setFrameFormat = (delegate* unmanaged[Stdcall]<nint, nint, uint, uint, void>)vtbl[VideoContextSetStreamFrameFormatIndex];
        setFrameFormat(videoContext, processor, streamIndex, frameFormat);
    }

    private static void SetStreamSourceRect(nint videoContext, nint processor, uint streamIndex, RECT rect)
    {
        var vtbl = *(nint**)videoContext;
        var setSourceRect = (delegate* unmanaged[Stdcall]<nint, nint, uint, int, RECT*, void>)vtbl[VideoContextSetStreamSourceRectIndex];
        setSourceRect(videoContext, processor, streamIndex, 1, &rect);
    }

    private static int VideoProcessorBlt(nint videoContext, nint processor, nint outputView, uint outputFrame, uint streamCount, D3D11_VIDEO_PROCESSOR_STREAM* streams)
    {
        var vtbl = *(nint**)videoContext;
        var blt = (delegate* unmanaged[Stdcall]<nint, nint, nint, uint, uint, D3D11_VIDEO_PROCESSOR_STREAM*, int>)vtbl[VideoContextVideoProcessorBltIndex];
        return blt(videoContext, processor, outputView, outputFrame, streamCount, streams);
    }

    private static void ReleaseIfNeeded(nint unknown)
    {
        if (unknown == 0)
        {
            return;
        }

        var vtbl = *(nint**)unknown;
        var release = (delegate* unmanaged[Stdcall]<nint, uint>)vtbl[ReleaseIndex];
        _ = release(unknown);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DXGI_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_VIDEO_PROCESSOR_CONTENT_DESC
    {
        public uint InputFrameFormat;
        public DXGI_RATIONAL InputFrameRate;
        public uint InputWidth;
        public uint InputHeight;
        public DXGI_RATIONAL OutputFrameRate;
        public uint OutputWidth;
        public uint OutputHeight;
        public uint Usage;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_QUERY_DESC
    {
        public uint Query;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_VIDEO_PROCESSOR_CAPS
    {
        public uint DeviceCaps;
        public uint FeatureCaps;
        public uint FilterCaps;
        public uint InputFormatCaps;
        public uint AutoStreamCaps;
        public uint StereoCaps;
        public uint RateConversionCapsCount;
        public uint MaxInputStreams;
        public uint MaxStreamStates;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_TEX2D_VPIV
    {
        public uint MipSlice;
        public uint ArraySlice;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC
    {
        public uint FourCC;
        public uint ViewDimension;
        public D3D11_TEX2D_VPIV Texture2D;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_TEX2D_VPOV
    {
        public uint MipSlice;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC
    {
        public uint ViewDimension;
        public D3D11_TEX2D_VPOV Texture2D;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_VIDEO_PROCESSOR_STREAM
    {
        public int Enable;
        public uint OutputIndex;
        public uint InputFrameOrField;
        public uint PastFrames;
        public uint FutureFrames;
        public nint ppPastSurfaces;
        public nint pInputSurface;
        public nint ppFutureSurfaces;
        public nint ppPastSurfacesRight;
        public nint pInputSurfaceRight;
        public nint ppFutureSurfacesRight;
    }
}
