using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI.Headless;

/// <summary>
/// One native message box request a headless session answered.
/// </summary>
/// <param name="Text">The message shown.</param>
/// <param name="Caption">The caption shown.</param>
/// <param name="Buttons">The button set requested.</param>
/// <param name="Icon">The icon requested.</param>
/// <param name="Result">The answer the session gave.</param>
public sealed record HeadlessMessageBoxCall(
    string Text,
    string Caption,
    NativeMessageBoxButtons Buttons,
    NativeMessageBoxIcon Icon,
    bool? Result);

/// <summary>
/// Answers native message boxes from a queue of scripted results and records every request.
/// </summary>
/// <remarks>
/// A request that finds the queue empty is answered with null, which the framework reads as
/// cancel; it is still recorded.
/// </remarks>
public sealed class HeadlessMessageBoxes : IMessageBoxService
{
    private readonly object _gate = new();
    private readonly Queue<bool?> _answers = new();
    private readonly List<HeadlessMessageBoxCall> _calls = new();

    internal HeadlessMessageBoxes()
    {
    }

    /// <summary>Gets the requests answered so far, oldest first.</summary>
    public IReadOnlyList<HeadlessMessageBoxCall> Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls.ToArray();
            }
        }
    }

    /// <summary>Queues the answer for the next message box: true for the affirmative button, false for the negative one, null for cancel.</summary>
    public void Enqueue(bool? result)
    {
        lock (_gate)
        {
            _answers.Enqueue(result);
        }
    }

    /// <summary>Drops queued answers and recorded requests.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _answers.Clear();
            _calls.Clear();
        }
    }

    bool IMessageBoxService.IsNativeDialogAvailable() => true;

    bool? IMessageBoxService.Show(nint owner, string text, string caption, NativeMessageBoxButtons buttons, NativeMessageBoxIcon icon)
    {
        lock (_gate)
        {
            bool? result = _answers.Count > 0 ? _answers.Dequeue() : null;
            _calls.Add(new HeadlessMessageBoxCall(text, caption, buttons, icon, result));
            return result;
        }
    }
}
