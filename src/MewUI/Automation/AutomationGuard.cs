namespace Aprillz.MewUI.Automation;

/// <summary>Checks a pattern action is allowed before a peer performs it.</summary>
internal static class AutomationGuard
{
    internal static void ThrowIfDisabled(AutomationPeer peer)
    {
        if (!peer.IsEnabled())
        {
            throw new InvalidOperationException($"'{peer.GetName()}' is disabled and cannot be operated.");
        }
    }

    internal static void ThrowIfReadOnly(bool isReadOnly, AutomationPeer peer)
    {
        if (isReadOnly)
        {
            throw new InvalidOperationException($"'{peer.GetName()}' is read-only.");
        }
    }
}
