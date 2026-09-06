namespace Aprillz.MewUI.Platform.Linux.X11;

/// <summary>
/// Result of <see cref="IX11InputMethod.ProcessKeyEvent"/>. <paramref name="IsKeyTranslation"/> tells whether
/// <paramref name="CommittedText"/> is the keystroke's own translation rather than an input method commit.
/// </summary>
internal readonly record struct X11ImeProcessResult(
    bool Handled,
    bool ForwardKeyToApp,
    string? CommittedText,
    bool IsKeyTranslation);
