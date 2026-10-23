namespace Aprillz.MewUI.Automation;

/// <summary>The state of a control that toggles.</summary>
public enum ToggleState
{
    Off,
    On,
    Indeterminate,
}

/// <summary>A control that cycles through on and off states, such as a check box.</summary>
public interface IToggleProvider
{
    ToggleState ToggleState { get; }

    /// <summary>Moves to the next state as a click would.</summary>
    void Toggle();
}
