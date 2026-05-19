using System.Collections.Generic;

namespace Aprillz.MewUI.Video.Sample.Decoding;

public sealed class VideoFrameQueue
{
    private readonly Queue<VideoFrame> _queue;
    private readonly int _capacity;
    private readonly object _gate = new();

    public VideoFrameQueue(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
        _queue = new Queue<VideoFrame>(capacity);
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _queue.Count;
            }
        }
    }

    public bool Enqueue(VideoFrame frame, Func<bool> shouldStop)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(shouldStop);

        lock (_gate)
        {
            while (_queue.Count >= _capacity && !shouldStop())
            {
                Monitor.Wait(_gate, TimeSpan.FromMilliseconds(50));
            }

            if (shouldStop())
            {
                return false;
            }

            _queue.Enqueue(frame);
            Monitor.PulseAll(_gate);
            return true;
        }
    }

    public VideoFrame? PullDue(TimeSpan clock, Action<VideoFrame> recycle, bool allowFutureFrame = false)
    {
        ArgumentNullException.ThrowIfNull(recycle);

        lock (_gate)
        {
            if (_queue.Count == 0)
            {
                return null;
            }

            while (_queue.Count >= 2)
            {
                var buffered = _queue.ToArray();
                var head = buffered[0];
                var next = buffered[1];
                if (next.Pts <= clock)
                {
                    recycle(_queue.Dequeue());
                    continue;
                }

                break;
            }

            if (_queue.Count == 0)
            {
                Monitor.PulseAll(_gate);
                return null;
            }

            var current = _queue.Peek();
            if (current.Pts > clock && !allowFutureFrame)
            {
                return null;
            }

            Monitor.PulseAll(_gate);
            return _queue.Dequeue();
        }
    }

    public VideoFrame? PullForPresentation(TimeSpan targetClock, Action<VideoFrame> recycle, bool allowFutureFrame = false)
    {
        ArgumentNullException.ThrowIfNull(recycle);

        lock (_gate)
        {
            if (_queue.Count == 0)
            {
                return null;
            }

            while (_queue.Count >= 2)
            {
                var buffered = _queue.ToArray();
                var head = buffered[0];
                var next = buffered[1];
                if (next.Pts <= targetClock)
                {
                    recycle(_queue.Dequeue());
                    continue;
                }

                break;
            }

            if (_queue.Count == 0)
            {
                Monitor.PulseAll(_gate);
                return null;
            }

            if (_queue.Count == 1)
            {
                var only = _queue.Peek();
                if (only.Pts > targetClock && !allowFutureFrame)
                {
                    return null;
                }

                Monitor.PulseAll(_gate);
                return _queue.Dequeue();
            }

            var current = _queue.Peek();

            if (current.Pts > targetClock && !allowFutureFrame)
            {
                return null;
            }

            if (allowFutureFrame)
            {
                Monitor.PulseAll(_gate);
                return _queue.Dequeue();
            }

            Monitor.PulseAll(_gate);
            return _queue.Dequeue();
        }
    }

    public void Clear(Action<VideoFrame> recycle)
    {
        ArgumentNullException.ThrowIfNull(recycle);

        lock (_gate)
        {
            while (_queue.Count > 0)
            {
                recycle(_queue.Dequeue());
            }

            Monitor.PulseAll(_gate);
        }
    }

    public void Pulse()
    {
        lock (_gate)
        {
            Monitor.PulseAll(_gate);
        }
    }
}