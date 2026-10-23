namespace Aprillz.MewUI.Automation;

/// <summary>A control whose value is a string, such as an edit box.</summary>
public interface IValueProvider
{
    string Value { get; }

    bool IsReadOnly { get; }

    /// <summary>Replaces the value as typing would; throws when the control is read-only or disabled.</summary>
    void SetValue(string value);
}
