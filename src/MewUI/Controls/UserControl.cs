namespace Aprillz.MewUI.Controls;

public class UserControl : ContentControl
{
    internal Element? GetBuiltContent()
    {
        HotReload.HotReloadRegistry.RegisterUserControl(this);
        return OnBuild();
    }

    protected void Build()
    {
        HotReload.HotReloadRegistry.RegisterUserControl(this);
        Content = OnBuild();
    }

    protected virtual Element? OnBuild()
    {
        return null;
    }
}
