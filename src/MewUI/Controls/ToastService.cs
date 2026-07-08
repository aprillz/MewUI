namespace Aprillz.MewUI.Controls;

/// <summary>
/// Overlay service for showing toast notifications.
/// Internally manages a <see cref="ToastPresenter"/> on the <see cref="OverlayLayer"/>.
/// </summary>
public sealed class ToastService : IOverlayService
{
    private readonly OverlayLayer _layer;
    private ToastPresenter? _presenter;

    internal ToastService(OverlayLayer layer)
    {
        _layer = layer;
    }

    /// <summary>
    /// Shows a toast notification. Auto-dismisses after a duration based on text length.
    /// </summary>
    public void Show(string text)
    {
        if (_presenter == null)
        {
            _presenter = new ToastPresenter();
            _presenter.BecameIdle += OnPresenterBecameIdle;
        }

        if (!_layer.Contains(_presenter))
            _layer.Add(_presenter);

        var displayText = text ?? string.Empty;
        _presenter.Show(displayText, ToastPresenter.ComputeDuration(displayText));
    }

    // The presenter stays measured/arranged/rendered every frame while it remains in the
    // overlay layer, so remove it once it has no content instead of leaving it there forever.
    private void OnPresenterBecameIdle()
    {
        if (_presenter != null)
            _layer.Remove(_presenter);
    }
}
