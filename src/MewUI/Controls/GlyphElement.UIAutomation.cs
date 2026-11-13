using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

partial class GlyphElement
{
    protected override AutomationPeer? OnCreateAutomationPeer() => new ImageAutomationPeer(this);
}
