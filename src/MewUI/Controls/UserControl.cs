namespace Aprillz.MewUI.Controls;

public class UserControl : ContentControl
{
    internal Element? GetBuiltContent() => OnBuild();

    protected void Build()
    {
        Content = OnBuild();
    }

    protected virtual Element? OnBuild()
    {
        return null;
    }
}
