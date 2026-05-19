using System.Collections.Concurrent;
using System.Diagnostics;

using Aprillz.MewUI.Video.Sample.Decoding;
using Aprillz.MewUI.Video.Sample.Diagnostics;

namespace Aprillz.MewUI.Video.Sample.Playback;

public sealed class VideoPlayback : IDisposable
{
    private static readonly TimeSpan PresentationLead = TimeSpan.FromMilliseconds(8);
    private static readonly TimeSpan LargePresentationErrorThreshold = TimeSpan.FromMilliseconds(8);

    private VideoDecoder _decoder;
    private readonly VideoFrameQueue _queue;
    private readonly PlaybackClock _clock;
    private readonly ConcurrentBag<VideoFrame> _framePool = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly AutoResetEvent _stateChanged = new(false);
    private readonly Thread _decodeThread;
    private readonly object _seekGate = new();
    private readonly object _decoderGate = new();

    private bool _isPlaying;
    private bool _seekPending;
    private TimeSpan _seekTarget;
    private bool _eofReached;
    private bool _disposed;
    private long _generation;
    private bool _firstQueuedFrameLogged;
    private bool _decodeSingleFrameWhilePaused;
    private bool _presentNextFrameAfterPausedSeek;
    private TimeSpan _lastPresentationTarget;
    private TimeSpan _lastPresentedFramePts;
    private TimeSpan _lastPresentationError;
    private TimeSpan _maxPresentationError;
    private long _presentationSampleCount;
    private long _presentationErrorSumTicks;
    private long _lastLargePresentationErrorLogTicks;

    public event Action? FrameReady;

    public VideoPlayback(string path, nint preferredD3D11Device = 0)
    {
        SampleLog.Write($"VideoPlayback ctor: {path}");
        _decoder = new VideoDecoder(path, preferredD3D11Device);
        _queue = new VideoFrameQueue(capacity: 4);
        _clock = new PlaybackClock();
        SourcePath = path;

        _decodeThread = new Thread(DecodeLoop)
        {
            IsBackground = true,
            Name = "MewUI Video Decode"
        };
        _decodeThread.Start();
        SampleLog.Write("Decode thread started.");
    }

    public string SourcePath { get; }

    public TimeSpan Duration { get { lock (_decoderGate) return _decoder.Duration; } }

    public TimeSpan Position => _clock.Now;

    public bool IsPlaying => _isPlaying;

    public bool IsEnded => _eofReached && _queue.Count == 0 && Position >= Duration;

    public int PixelWidth { get { lock (_decoderGate) return _decoder.Width; } }

    public int PixelHeight { get { lock (_decoderGate) return _decoder.Height; } }

    public string DecoderStatsOverlayText { get { lock (_decoderGate) return _decoder.GetStatsOverlayText(); } }

    public nint D3D11Device { get { lock (_decoderGate) return _decoder.D3D11Device; } }

    public nint MetalDevice { get { lock (_decoderGate) return _decoder.MetalDevice; } }

    /// <summary>
    /// Forwards <see cref="VideoDecoder.ForceCpuReadback"/> so the sample UI can
    /// toggle the path used for upload-side benchmarking (sync vs PBO+fence).
    /// </summary>
    public bool ForceCpuReadback
    {
        get { lock (_decoderGate) return _decoder.ForceCpuReadback; }
        set { lock (_decoderGate) _decoder.ForceCpuReadback = value; }
    }

    public long Generation => Interlocked.Read(ref _generation);

    public string PresentationTimingStatsText
    {
        get
        {
            long sampleCount = _presentationSampleCount;
            TimeSpan averageError = sampleCount > 0
                ? TimeSpan.FromTicks(_presentationErrorSumTicks / sampleCount)
                : TimeSpan.Zero;

            return $"target={FormatSignedMilliseconds(_lastPresentationTarget)} pts={FormatSignedMilliseconds(_lastPresentedFramePts)} err={FormatSignedMilliseconds(_lastPresentationError)} avg|err|={FormatMilliseconds(averageError)} max|err|={FormatMilliseconds(_maxPresentationError)}";
        }
    }

    public void Play()
    {
        ThrowIfDisposed();
        SampleLog.Write("VideoPlayback.Play");

        if (Duration > TimeSpan.Zero && Position >= Duration)
        {
            Seek(TimeSpan.Zero);
        }

        _isPlaying = true;
        _clock.Start();
        _stateChanged.Set();
    }

    public void Pause()
    {
        if (_disposed)
        {
            return;
        }

        SampleLog.Write("VideoPlayback.Pause");
        _isPlaying = false;
        _clock.Pause();
        _stateChanged.Set();
    }

    public void Seek(TimeSpan target)
    {
        ThrowIfDisposed();

        if (target < TimeSpan.Zero)
        {
            target = TimeSpan.Zero;
        }

        if (Duration > TimeSpan.Zero && target > Duration)
        {
            target = Duration;
        }

        lock (_seekGate)
        {
            _seekTarget = target;
            _seekPending = true;
        }

        if (!_isPlaying)
        {
            _decodeSingleFrameWhilePaused = true;
            _presentNextFrameAfterPausedSeek = true;
        }

        _clock.SeekTo(target);
        _eofReached = false;
        Interlocked.Increment(ref _generation);
        _queue.Pulse();
        _stateChanged.Set();
    }

    public VideoFrame? PullCurrent()
    {
        if (_disposed)
        {
            return null;
        }

        TimeSpan targetPresentationTime = _clock.Now;
        if (_isPlaying)
        {
            targetPresentationTime += PresentationLead;
        }

        var frame = _queue.PullForPresentation(targetPresentationTime, Recycle, allowFutureFrame: _presentNextFrameAfterPausedSeek);
        if (frame is not null)
        {
            _presentNextFrameAfterPausedSeek = false;
            RecordPresentationTiming(targetPresentationTime, frame.Pts);
            return frame;
        }

        if (_isPlaying && IsEnded)
        {
            SampleLog.Write("Playback reached EOF. Stopping playback.");
            _isPlaying = false;
            _clock.Pause();
            _stateChanged.Set();
        }

        return null;
    }

    public void Recycle(VideoFrame frame)
    {
        frame.ResetGpuState();
        if (_disposed) return;
        _framePool.Add(frame);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        SampleLog.Write("Disposing VideoPlayback.");
        _disposed = true;
        _isPlaying = false;
        _cts.Cancel();
        _queue.Pulse();
        _stateChanged.Set();
        _decodeThread.Join();
        _stateChanged.Dispose();
        _queue.Clear(frame => frame.ResetGpuState());
        lock (_decoderGate)
        {
            _decoder.Dispose();
        }
        _cts.Dispose();
    }

    public void RecreateDecoder(nint preferredD3D11Device, TimeSpan position)
    {
        ThrowIfDisposed();
        SampleLog.Write($"VideoPlayback.RecreateDecoder position={position}");

        var replacement = new VideoDecoder(SourcePath, preferredD3D11Device);
        if (position > TimeSpan.Zero)
        {
            var clampedPosition = replacement.Duration > TimeSpan.Zero && position > replacement.Duration
                ? replacement.Duration
                : position;
            replacement.Seek(clampedPosition);
            position = clampedPosition;
        }

        VideoDecoder oldDecoder;
        lock (_decoderGate)
        {
            oldDecoder = _decoder;
            _decoder = replacement;
            _queue.Clear(Recycle);
            _eofReached = false;
            _firstQueuedFrameLogged = false;
            _decodeSingleFrameWhilePaused = !_isPlaying;
            _presentNextFrameAfterPausedSeek = true;
            Interlocked.Increment(ref _generation);
        }

        oldDecoder.Dispose();
        _clock.SeekTo(position);
        _stateChanged.Set();
        FrameReady?.Invoke();
    }

    private void DecodeLoop()
    {
        SampleLog.Write("Decode loop entered.");
        while (!_cts.IsCancellationRequested)
        {
            ProcessPendingSeek();
            if (!_isPlaying && !_decodeSingleFrameWhilePaused)
            {
                _stateChanged.WaitOne(50);
                continue;
            }

            ProcessPendingSeek();

            int decodedWidth;
            int decodedHeight;
            var frame = RentFrame();
            bool decoded;
            TimeSpan pts;
            IGpuFrameResource? gpuResource;
            bool hasCpuPixels;
            lock (_decoderGate)
            {
                decoded = _decoder.TryDecodeNext(frame.BgraData, out pts, out gpuResource, out hasCpuPixels);
                decodedWidth = _decoder.Width;
                decodedHeight = _decoder.Height;
            }

            if (!decoded)
            {
                Recycle(frame);
                _eofReached = true;
                SampleLog.Write("Decoder reported EOF or no more frames.");
                _stateChanged.WaitOne(50);
                continue;
            }

            frame.Pts = pts;
            frame.Width = decodedWidth;
            frame.Height = decodedHeight;
            frame.ResetGpuState();
            frame.HasCpuPixels = hasCpuPixels;
            frame.GpuResource = gpuResource;
            if (!_firstQueuedFrameLogged)
            {
                _firstQueuedFrameLogged = true;
                SampleLog.Write($"First frame queued. pts={pts}, size={frame.Width}x{frame.Height}");
            }

            if (!_queue.Enqueue(frame, () => _cts.IsCancellationRequested || _seekPending || _disposed))
            {
                Recycle(frame);
            }
            else
            {
                // Fire FrameReady on every queued frame regardless of play state. The
                // view uses this to drive InvalidateVisual at the actual decode rate,
                // replacing the previous "always invalidate at end of OnRender" loop
                // (which spun the render thread at ~5x video framerate, burning CPU
                // re-sampling the same texture).
                FrameReady?.Invoke();
            }

            _decodeSingleFrameWhilePaused = false;
        }
    }

    private void ProcessPendingSeek()
    {
        if (!_seekPending)
        {
            return;
        }

        TimeSpan target;
        lock (_seekGate)
        {
            if (!_seekPending)
            {
                return;
            }

            target = _seekTarget;
            _seekPending = false;
        }

        lock (_decoderGate)
        {
            _decoder.Seek(target);
        }
        _queue.Clear(Recycle);
        _eofReached = false;
    }

    private VideoFrame RentFrame()
    {
        int requiredBytes;
        int width;
        int height;
        lock (_decoderGate)
        {
            width = _decoder.Width;
            height = _decoder.Height;
            requiredBytes = checked(width * height * 4);
        }
        if (_framePool.TryTake(out var frame))
        {
            frame.ResetGpuState();
            if (frame.BgraData.Length < requiredBytes)
            {
                frame.BgraData = new byte[requiredBytes];
            }

            return frame;
        }

        return new VideoFrame
        {
            BgraData = new byte[requiredBytes],
            Width = width,
            Height = height
        };
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void EnableCpuPresentationFallback()
    {
        if (_disposed)
        {
            return;
        }

        lock (_decoderGate)
        {
            _decoder.EnableCpuReadbackFallback();
        }
        _queue.Clear(Recycle);

        if (!_isPlaying)
        {
            _decodeSingleFrameWhilePaused = true;
            _presentNextFrameAfterPausedSeek = true;
        }

        _stateChanged.Set();
    }

    private void RecordPresentationTiming(TimeSpan targetPresentationTime, TimeSpan framePts)
    {
        TimeSpan presentationError = framePts - targetPresentationTime;
        TimeSpan absoluteError = TimeSpan.FromTicks(Math.Abs(presentationError.Ticks));

        _lastPresentationTarget = targetPresentationTime;
        _lastPresentedFramePts = framePts;
        _lastPresentationError = presentationError;
        if (absoluteError > _maxPresentationError)
        {
            _maxPresentationError = absoluteError;
        }

        _presentationSampleCount++;
        _presentationErrorSumTicks += absoluteError.Ticks;

        long nowTicks = Stopwatch.GetTimestamp();
        if (absoluteError >= LargePresentationErrorThreshold
            && (nowTicks - _lastLargePresentationErrorLogTicks) >= Stopwatch.Frequency)
        {
            _lastLargePresentationErrorLogTicks = nowTicks;
            //SampleLog.Write(
            //    $"Presentation timing error: target={FormatSignedMilliseconds(targetPresentationTime)}, pts={FormatSignedMilliseconds(framePts)}, err={FormatSignedMilliseconds(presentationError)}");
        }
    }

    private static string FormatMilliseconds(TimeSpan value)
        => $"{value.TotalMilliseconds:0.000}ms";

    private static string FormatSignedMilliseconds(TimeSpan value)
        => $"{value.TotalMilliseconds:+0.000;-0.000;0.000}ms";
}
