namespace Aprillz.MewUI.Controls;

public class UserControl : ContentControl
{
    // Lazy-build gate: one attempt only, so an intentional null OnBuild is not retried every layout.
    private bool _buildAttempted;

    internal Element? GetBuiltContent()
    {
        HotReload.HotReloadRegistry.RegisterUserControl(this);
        return OnBuild();
    }

    /// <summary>
    /// Builds the content from <see cref="OnBuild"/> immediately. Optional: the first layout pass
    /// builds lazily; call this only when content is needed before layout runs.
    /// </summary>
    protected void Build()
    {
        _buildAttempted = true;
        HotReload.HotReloadRegistry.RegisterUserControl(this);
        Content = OnBuild();
    }

    protected virtual Element? OnBuild()
    {
        return null;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // First layout runs after the derived constructor completed, so OnBuild sees initialized
        // fields; externally assigned Content (and an explicit Build()) win over the lazy pass.
        if (!_buildAttempted)
        {
            _buildAttempted = true;
            if (Content == null)
            {
                Build();
            }
        }

        return base.MeasureOverride(availableSize);
    }
}
