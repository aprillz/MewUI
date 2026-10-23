namespace Aprillz.MewUI.Windowless.Sample;

/// <summary>The platform side of a <see cref="TrayIcon"/>.</summary>
internal interface ITrayIconBackend : IDisposable
{
    string Name { get; }

    /// <summary>
    /// Adds the icon. Reads <see cref="TrayIcon.ContextMenu"/> again each time the menu opens, so changes to it show.
    /// Called on the UI thread; clicks and commands are delivered there too.
    /// </summary>
    void Start(TrayIcon owner);
}
