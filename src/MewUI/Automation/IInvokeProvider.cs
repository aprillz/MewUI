namespace Aprillz.MewUI.Automation;

/// <summary>A control that performs one action when invoked, such as a button.</summary>
public interface IInvokeProvider
{
    /// <summary>Performs the action as a click would.</summary>
    void Invoke();
}
