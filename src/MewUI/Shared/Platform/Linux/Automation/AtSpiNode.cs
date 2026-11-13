using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Platform.Linux.Automation;

/// <summary>The accessibility bus object of one automation peer: the peer and the object path clients address it by.</summary>
internal sealed class AtSpiNode
{
    internal AtSpiNode(AutomationPeer peer, string path)
    {
        Peer = peer;
        Path = path;
    }

    internal AutomationPeer Peer { get; }

    internal string Path { get; }

    /// <summary>The text last announced, from which the next change is worked out; null for a peer without text.</summary>
    internal string? LastText { get; set; }

    /// <summary>The actions AT-SPI2 lists for the peer, in the order clients index them.</summary>
    internal IReadOnlyList<string> Actions()
    {
        var actions = new List<string>(2);
        if (Peer is IInvokeProvider)
        {
            actions.Add("click");
        }
        else if (Peer is IToggleProvider)
        {
            actions.Add("toggle");
        }
        else if (Peer is ISelectionItemProvider)
        {
            actions.Add("select");
        }

        if (Peer is IExpandCollapseProvider expander && expander.ExpandCollapseState != ExpandCollapseState.LeafNode)
        {
            actions.Add("expand or collapse");
        }

        return actions;
    }

    /// <summary>Runs the named action; false when the peer refuses or is disabled.</summary>
    internal bool DoAction(string action)
    {
        if (!Peer.IsEnabled())
        {
            return false;
        }

        switch (action)
        {
            case "click" when Peer is IInvokeProvider invoke:
                // The action must not hold the reader's call while what it starts (a dialog loop) runs.
                Application.Current.Dispatcher?.BeginInvoke(DispatcherPriority.Normal, () =>
                {
                    try
                    {
                        invoke.Invoke();
                    }
                    catch (InvalidOperationException)
                    {
                        // The element changed (disabled, removed) after the reader was told the action ran.
                    }
                });
                return true;
            case "toggle" when Peer is IToggleProvider toggle:
                toggle.Toggle();
                return true;
            case "select" when Peer is ISelectionItemProvider item:
                item.Select();
                return true;
            case "expand or collapse" when Peer is IExpandCollapseProvider expander:
                if (expander.ExpandCollapseState == ExpandCollapseState.Expanded)
                {
                    expander.Collapse();
                }
                else
                {
                    expander.Expand();
                }

                return true;
            default:
                return false;
        }
    }
}
