// Ported from Windows Terminal src/buffer/out/cursor.h, cursor.cpp and the CursorType of src/inc/conattrs.hpp (MIT).

using Aprillz.MewUI.Terminal.Types;

namespace Aprillz.MewUI.Terminal.Buffer;

internal enum CursorType
{
    Legacy = 0,
    VerticalBar = 1,
    Underscore = 2,
    EmptyBox = 3,
    FullBox = 4,
    DoubleUnderscore = 5,
}

/// <summary>
/// The cursor of a text buffer: position, shape, visibility and blinking, and the position a delayed end-of-line
/// wrap was deferred at.
/// </summary>
internal sealed class Cursor
{
    // Large enough to be one pixel on a six pixel font.
    public const int CURSOR_SMALL_SIZE = 25;

    private readonly TextBuffer _parentBuffer;
    private ulong _mutationId;
    private TilPoint _position;
    private TilPoint? _delayedAt;
    private int _size;
    private CursorType _cursorType = CursorType.Legacy;
    private bool _isVisible = true;
    private bool _isBlinking = true;
    private bool _isDouble;

    public Cursor(int size, TextBuffer parentBuffer)
    {
        _size = size;
        _parentBuffer = parentBuffer;
    }

    public ulong LastMutationId => _mutationId;

    public TilPoint Position => _position;

    public bool IsVisible => _isVisible;

    public bool IsBlinking => _isBlinking;

    public bool IsDouble => _isDouble;

    public int Size => _size;

    public CursorType Type => _cursorType;

    /// <summary>Where a wrap at the end of a line was deferred, until the next character or cursor move.</summary>
    public TilPoint? DelayEolWrap => _delayedAt;

    public void SetIsVisible(bool enable)
    {
        if (_isVisible != enable)
        {
            _isVisible = enable;
            RedrawIfVisible();
        }
    }

    public void SetIsBlinking(bool enable)
    {
        if (_isBlinking != enable)
        {
            _isBlinking = enable;
            RedrawIfVisible();
        }
    }

    public void SetIsDouble(bool isDouble)
    {
        if (_isDouble != isDouble)
        {
            _isDouble = isDouble;
            RedrawIfVisible();
        }
    }

    public void SetSize(int size)
    {
        if (_size != size)
        {
            _size = size;
            RedrawIfVisible();
        }
    }

    public void SetStyle(int size, CursorType type)
    {
        if (_size != size || _cursorType != type)
        {
            _size = size;
            _cursorType = type;
            RedrawIfVisible();
        }
    }

    public void SetPosition(TilPoint position)
    {
        ResetDelayEolWrap();
        if (_position != position)
        {
            _position = position;
            RedrawIfVisible();
        }
    }

    public void SetXPosition(int newX)
    {
        ResetDelayEolWrap();
        if (_position.X != newX)
        {
            _position = _position with { X = newX };
            RedrawIfVisible();
        }
    }

    public void SetYPosition(int newY)
    {
        ResetDelayEolWrap();
        if (_position.Y != newY)
        {
            _position = _position with { Y = newY };
            RedrawIfVisible();
        }
    }

    public void IncrementXPosition(int deltaX)
    {
        ResetDelayEolWrap();
        if (deltaX != 0)
        {
            _position = _position with { X = _position.X + deltaX };
            RedrawIfVisible();
        }
    }

    public void IncrementYPosition(int deltaY)
    {
        ResetDelayEolWrap();
        if (deltaY != 0)
        {
            _position = _position with { Y = _position.Y + deltaY };
            RedrawIfVisible();
        }
    }

    public void DecrementXPosition(int deltaX) => IncrementXPosition(-deltaX);

    public void DecrementYPosition(int deltaY) => IncrementYPosition(-deltaY);

    public void CopyProperties(Cursor other)
    {
        _position = other._position;
        _delayedAt = other._delayedAt;
        _size = other._size;
        _cursorType = other._cursorType;
        _isVisible = other._isVisible;
        _isBlinking = other._isBlinking;
        _isDouble = other._isDouble;
    }

    public void DelayEolWrapAtCurrentPosition() => _delayedAt = _position;

    public void ResetDelayEolWrap() => _delayedAt = null;

    public void SetType(CursorType type) => _cursorType = type;

    private void RedrawIfVisible()
    {
        _mutationId++;
        if (_isVisible)
        {
            _parentBuffer.NotifyPaintFrame();
        }
    }
}
