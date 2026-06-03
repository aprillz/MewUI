using System.Globalization;
using System.Resources;

namespace Aprillz.MewUI;

/// <summary>
/// Centralized UI strings for localization.
/// Default values are English. Assign <see cref="ObservableValue{T}.Value"/> or call
/// <see cref="SetCulture(CultureInfo?)"/> at runtime to update all bound UI.
/// </summary>
public static class MewUIStrings
{
    private static readonly ResourceManager ResourceManager =
        new("Aprillz.MewUI.Resources.MewUIStrings", typeof(MewUIStrings).Assembly);

    private static readonly object SyncRoot = new();
    private static readonly List<LocalizedStringEntry> Entries = [];
    private static CultureInfo? _culture;

    // MessageBox
    public static ObservableValue<string> Information { get; } = Register(nameof(Information), "Information");

    public static ObservableValue<string> Warning { get; } = Register(nameof(Warning), "Warning");

    public static ObservableValue<string> Error { get; } = Register(nameof(Error), "Error");

    public static ObservableValue<string> Question { get; } = Register(nameof(Question), "Confirm");

    public static ObservableValue<string> Success { get; } = Register(nameof(Success), "Success");

    public static ObservableValue<string> Shield { get; } = Register(nameof(Shield), "Security");

    public static ObservableValue<string> Crash { get; } = Register(nameof(Crash), "Crash");

    public static ObservableValue<string> ShowDetail { get; } = Register(nameof(ShowDetail), "Show _Details");

    public static ObservableValue<string> OK { get; } = Register(nameof(OK), "_OK");

    public static ObservableValue<string> Cancel { get; } = Register(nameof(Cancel), "_Cancel");

    public static ObservableValue<string> Yes { get; } = Register(nameof(Yes), "_Yes");

    public static ObservableValue<string> No { get; } = Register(nameof(No), "_No");

    public static ObservableValue<string> Retry { get; } = Register(nameof(Retry), "_Retry");

    public static ObservableValue<string> Ignore { get; } = Register(nameof(Ignore), "_Ignore");

    // BusyIndicator
    public static ObservableValue<string> Abort { get; } = Register(nameof(Abort), "Abort");

    public static ObservableValue<string> AbortConfirmation { get; } = Register(nameof(AbortConfirmation), "Are you sure you want to abort this operation?");

    public static ObservableValue<string> Aborting { get; } = Register(nameof(Aborting), "Aborting...");

    // Text editing
    public static ObservableValue<string> Undo { get; } = Register(nameof(Undo), "Undo");

    public static ObservableValue<string> Redo { get; } = Register(nameof(Redo), "Redo");

    public static ObservableValue<string> Cut { get; } = Register(nameof(Cut), "Cut");

    public static ObservableValue<string> Copy { get; } = Register(nameof(Copy), "Copy");

    public static ObservableValue<string> Paste { get; } = Register(nameof(Paste), "Paste");

    public static ObservableValue<string> SelectAll { get; } = Register(nameof(SelectAll), "Select All");

    static MewUIStrings()
    {
        SetCulture();
    }

    /// <summary>
    /// Gets the explicitly applied culture. A null value means the last <see cref="SetCulture(CultureInfo?)"/>
    /// call used <see cref="CultureInfo.CurrentUICulture"/>.
    /// </summary>
    public static CultureInfo? Culture
    {
        get
        {
            lock (SyncRoot)
            {
                return _culture;
            }
        }
    }

    /// <summary>
    /// Applies localized values from embedded resources. Pass <c>null</c> to use
    /// <see cref="CultureInfo.CurrentUICulture"/>.
    /// </summary>
    /// <param name="culture">Culture to apply, or null for the current UI culture.</param>
    public static void SetCulture(CultureInfo? culture = null)
    {
        CultureInfo resolvedCulture = culture ?? CultureInfo.CurrentUICulture;

        lock (SyncRoot)
        {
            _culture = culture;
            for (int i = 0; i < Entries.Count; i++)
            {
                LocalizedStringEntry entry = Entries[i];
                entry.Value.Value = ResourceManager.GetString(entry.Name, resolvedCulture) ?? entry.DefaultValue;
            }
        }
    }

    private static ObservableValue<string> Register(string name, string defaultValue)
    {
        var value = new ObservableValue<string>(defaultValue);
        Entries.Add(new LocalizedStringEntry(name, defaultValue, value));
        return value;
    }

    private sealed record LocalizedStringEntry(
        string Name,
        string DefaultValue,
        ObservableValue<string> Value);
}
