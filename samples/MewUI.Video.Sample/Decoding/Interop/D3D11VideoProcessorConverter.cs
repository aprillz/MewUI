using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Video.Sample.Decoding;

internal sealed unsafe class D3D11VideoProcessorConverter : IDisposable
{
    private const int OutputTexturePoolSize = 6;
    private const int QueryInterfaceIndex = 0;
    private const int AddRefIndex = 1;
    private const int ReleaseIndex = 2;

    private const int DeviceCreateTexture2DIndex = 5;
    private const int DeviceGetImmediateContextIndex = 40;

    private const int VideoDeviceCreateVideoProcessorIndex = 4;
    private const int VideoDeviceCreateVideoProcessorInputViewIndex = 8;
    private const int VideoDeviceCreateVideoProcessorOutputViewIndex = 9;
    private const int VideoDeviceCreateVideoProcessorEnumeratorIndex = 10;

    private const int VideoContextSetStreamFrameFormatIndex = 27;
    private const int VideoContextVideoProcessorBltIndex = 53;

    private const uint D3D11_USAGE_DEFAULT = 0;
    private const uint D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE = 0;
    private const uint D3D11_VIDEO_USAGE_PLAYBACK_NORMAL = 0;
    private const uint D3D11_VPIV_DIMENSION_TEXTURE2D = 1;
    private const uint D3D11_VPOV_DIMENSION_TEXTURE2D = 1;
    private const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;

    private static readonly Guid IID_ID3D11VideoDevice = new("10EC4D5B-975A-4689-B9E4-D0AAC30FE333");
    private static readonly Guid IID_ID3D11VideoContext = new("61F21C45-3C0E-4A74-9CEA-67100D9AD5E4");

    private readonly nint _device;
    private readonly nint _deviceContext;
    private readonly nint _videoDevice;
    private readonly nint _videoContext;
    private readonly Queue<nint> _availableOutputTextures = new();
    private readonly object _outputTextureGate = new();

    private nint _processorEnumerator;
    private nint _videoProcessor;
    private uint _inputFormat;
    private uint _inputWidth;
    private uint _inputHeight;
    private uint _outputWidth;
    private uint _outputHeight;
    private bool _disposed;

    private D3D11VideoProcessorConverter(nint device, nint deviceContext, nint videoDevice, nint videoContext)
    {
        _device = device;
        _deviceContext = deviceContext;
        _videoDevice = videoDevice;
        _videoContext = videoContext;
    }

    public static bool TryCreate(nint device, out D3D11VideoProcessorConverter? converter)
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
            GetImmediateContext(device, out deviceContext);
            if (deviceContext == 0)
            {
                return false;
            }

            if (QueryInterface(device, IID_ID3D11VideoDevice, out videoDevice) < 0 || videoDevice == 0)
            {
                return false;
            }

            if (QueryInterface(deviceContext, IID_ID3D11VideoContext, out videoContext) < 0 || videoContext == 0)
            {
                return false;
            }

            converter = new D3D11VideoProcessorConverter(device, deviceContext, videoDevice, videoContext);
            return true;
        }
        catch
        {
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
            return false;
        }

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

            SetStreamFrameFormat(_videoContext, _videoProcessor, 0, D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE);

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

            if (VideoProcessorBlt(_videoContext, _videoProcessor, outputView, 0, 1, &stream) < 0)
            {
                return false;
            }

            D3D11Native.FlushDeviceContext(_deviceContext);
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
                ReleaseIfNeeded(outputTexture);
                return;
            }

            if (_availableOutputTextures.Count >= OutputTexturePoolSize)
            {
                ReleaseIfNeeded(outputTexture);
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

        if (CreateVideoProcessorEnumerator(_videoDevice, &desc, out _processorEnumerator) < 0 || _processorEnumerator == 0)
        {
            _processorEnumerator = 0;
            return false;
        }

        if (CreateVideoProcessor(_videoDevice, _processorEnumerator, 0, out _videoProcessor) < 0 || _videoProcessor == 0)
        {
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
            Format = DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDescCount = 1,
            SampleDescQuality = 0,
            Usage = D3D11_USAGE_DEFAULT,
            BindFlags = D3D11Native.D3D11_BIND_SHADER_RESOURCE | D3D11Native.D3D11_BIND_RENDER_TARGET,
            CPUAccessFlags = 0,
            MiscFlags = 0,
        };

        outputTexture = 0;
        return CreateTexture2D(_device, &desc, 0, out outputTexture) >= 0 && outputTexture != 0;
    }

    private bool TryCreateInputView(nint inputTexture, int arraySlice, uint format, out nint inputView)
    {
        D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC desc = new()
        {
            FourCC = format,
            ViewDimension = D3D11_VPIV_DIMENSION_TEXTURE2D,
            Texture2D = new D3D11_TEX2D_VPIV
            {
                MipSlice = 0,
                ArraySlice = unchecked((uint)arraySlice),
            },
        };

        inputView = 0;
        return CreateVideoProcessorInputView(_videoDevice, inputTexture, _processorEnumerator, &desc, out inputView) >= 0 && inputView != 0;
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
        return CreateVideoProcessorOutputView(_videoDevice, outputTexture, _processorEnumerator, &desc, out outputView) >= 0 && outputView != 0;
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
                ReleaseIfNeeded(_availableOutputTextures.Dequeue());
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

    private static void SetStreamFrameFormat(nint videoContext, nint processor, uint streamIndex, uint frameFormat)
    {
        var vtbl = *(nint**)videoContext;
        var setFrameFormat = (delegate* unmanaged[Stdcall]<nint, nint, uint, uint, void>)vtbl[VideoContextSetStreamFrameFormatIndex];
        setFrameFormat(videoContext, processor, streamIndex, frameFormat);
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
