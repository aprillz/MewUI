using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI;

public partial class Window
{
    public static readonly MewProperty<SystemBackdrop> SystemBackdropProperty =
        MewProperty<SystemBackdrop>.Register<Window>(nameof(SystemBackdrop), SystemBackdrop.None, MewPropertyOptions.AffectsRender,
            static (self, _, _) => self.PushSystemBackdrop());

    private SystemBackdropArea? _systemBackdropArea;

    /// <summary>
    /// Gets or sets the material the platform shows behind the transparent parts of the window. It applies only while
    /// <see cref="AllowsTransparency"/> is set; a platform that cannot show the material lowers it to the nearest one it
    /// can, and where it can show none <see cref="UIElement.BackdropFallbackBrush"/> is drawn under the background.
    /// </summary>
    public SystemBackdrop SystemBackdrop
    {
        get => GetValue(SystemBackdropProperty);
        set => SetValue(SystemBackdropProperty, value);
    }

    /// <summary>The material the backend shows now.</summary>
    internal SystemBackdrop ActualSystemBackdrop { get; private set; }

    /// <summary>Whether a frame fills the window with the fallback brush because the asked-for material is not showing.</summary>
    private bool DrawsBackdropFallback
        => SystemBackdrop != SystemBackdrop.None && ActualSystemBackdrop == SystemBackdrop.None && BackdropFallbackBrush != null;

    /// <summary>Records the material the backend applied.</summary>
    internal void SetActualSystemBackdrop(SystemBackdrop material)
    {
        if (ActualSystemBackdrop == material)
        {
            return;
        }

        ActualSystemBackdrop = material;
        InvalidateVisual();
        OnActualSystemBackdropChanged();
    }

    /// <summary>Called when the material the backend shows has changed.</summary>
    private protected virtual void OnActualSystemBackdropChanged()
    {
    }

    /// <summary>Limits the material to part of the client area; null fills all of it.</summary>
    internal void SetSystemBackdropArea(SystemBackdropArea? area)
    {
        if (_systemBackdropArea == area)
        {
            return;
        }

        _systemBackdropArea = area;
        PushSystemBackdrop();
    }

    private protected void PushSystemBackdrop()
    {
        // Without per-pixel transparency the window covers whatever a material would show.
        var requested = AllowsTransparency ? SystemBackdrop : SystemBackdrop.None;
        if (_backend is IWindowSystemBackdropBackend materialBackend)
        {
            materialBackend.SetSystemBackdrop(requested, _systemBackdropArea);
        }
        else
        {
            SetActualSystemBackdrop(SystemBackdrop.None);
        }
    }
}
