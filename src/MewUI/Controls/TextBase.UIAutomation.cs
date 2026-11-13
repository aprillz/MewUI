using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

partial class TextBase
{
    protected override AutomationPeer? OnCreateAutomationPeer() => new TextBoxAutomationPeer(this);
}
