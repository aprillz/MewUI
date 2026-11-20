using System.Runtime.CompilerServices;
using System.Text;

using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Platform;

internal enum NativeMenuEntryKind
{
    Item,
    Separator,
    SubMenu,
}

/// <summary>One entry of a menu as a native menu shows it, read when the menu opens.</summary>
internal sealed class NativeMenuEntry
{
    internal static readonly NativeMenuEntry Separator = new() { Kind = NativeMenuEntryKind.Separator };

    public NativeMenuEntryKind Kind { get; init; }

    /// <summary>The text without access key markers.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>The position of the access key in <see cref="Text"/>, or -1.</summary>
    public int AccessKeyIndex { get; init; } = -1;

    public bool IsEnabled { get; init; }

    public bool IsCheckable { get; init; }

    public bool IsChecked { get; init; }

    /// <summary>The single gesture the item's command answers to, or null.</summary>
    public KeyGesture? Gesture { get; init; }

    public NativeMenuIcon? Icon { get; init; }

    public IReadOnlyList<NativeMenuEntry> Children { get; init; } = [];

    public MenuItem? Item { get; init; }

    /// <summary>
    /// The text with <paramref name="marker"/> before the access key and every literal <paramref name="marker"/>
    /// doubled; a platform without access keys passes <c>'\0'</c> and gets <see cref="Text"/>.
    /// </summary>
    public string FormatText(char marker)
    {
        if (marker == '\0')
        {
            return Text;
        }

        var builder = new StringBuilder(Text.Length + 2);
        for (int index = 0; index < Text.Length; index++)
        {
            char current = Text[index];
            if (index == AccessKeyIndex)
            {
                builder.Append(marker);
            }

            if (current == marker)
            {
                builder.Append(marker);
            }

            builder.Append(current);
        }

        return builder.ToString();
    }
}

/// <summary>A menu icon drawn to premultiplied BGRA pixels.</summary>
internal sealed class NativeMenuIcon(int pixelSize, double scale, byte[] premultipliedBgra, bool isGlyph)
{
    public int PixelSize { get; } = pixelSize;

    public double Scale { get; } = scale;

    public byte[] PremultipliedBgra { get; } = premultipliedBgra;

    /// <summary>Whether the icon is drawn only in its foreground color, so a platform may tint it.</summary>
    public bool IsGlyph { get; } = isGlyph;
}

/// <summary>
/// Reads a <see cref="Menu"/> into what a native menu shows, resolving commands against one router and target, and
/// runs the entry chosen from it. Used on the UI thread.
/// </summary>
internal sealed class NativeMenuSnapshot
{
    // Icons depend only on the template and the pixel size: they are drawn detached, in the default foreground.
    private static readonly ConditionalWeakTable<IconTemplate, Dictionary<int, NativeMenuIcon?>> _icons = new();

    private readonly CommandRouter _router;
    private readonly CommandTarget _target;
    private readonly Window? _gestureWindow;
    private readonly double _iconSizeDip;
    private readonly double _iconScale;

    /// <param name="router">The router the entries' commands resolve and run through.</param>
    /// <param name="target">The target they resolve against.</param>
    /// <param name="gestureWindow">The window whose input maps label the gestures, or null for the application's map alone.</param>
    /// <param name="iconSizeDip">The icon size the native menu shows.</param>
    /// <param name="iconScale">The pixels per DIP of the screen the menu opens on.</param>
    public NativeMenuSnapshot(CommandRouter router, CommandTarget target, Window? gestureWindow, double iconSizeDip, double iconScale)
    {
        _router = router;
        _target = target;
        _gestureWindow = gestureWindow;
        _iconSizeDip = iconSizeDip;
        _iconScale = iconScale > 0 ? iconScale : 1;
    }

    public IReadOnlyList<NativeMenuEntry> Read(Menu menu)
    {
        var entries = new List<NativeMenuEntry>(menu.Items.Count);
        foreach (var entry in menu.Items)
        {
            if (entry is MenuItem item)
            {
                entries.Add(ReadItem(item));
            }
            else
            {
                entries.Add(NativeMenuEntry.Separator);
            }
        }

        return RemoveStraySeparators(entries);
    }

    /// <summary>Runs the entry's command on a later dispatcher turn, if it can still run then.</summary>
    public void Invoke(NativeMenuEntry entry)
    {
        var item = entry.Item;
        var command = item?.Command;
        var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
        if (item == null || command == null || dispatcher == null)
        {
            return;
        }

        object? data = item.CommandData;

        // The native menu is still unwinding, and the command may remove the surface that owns it.
        dispatcher.BeginInvoke(() =>
        {
            if (item.IsEnabled && (item.CanClick?.Invoke() ?? true))
            {
                _router.TryExecuteFromInput(command, _target, source: null, data);
            }
        });
    }

    private NativeMenuEntry ReadItem(MenuItem item)
    {
        var (text, _, accessKeyIndex) = item.GetParsedText();
        var icon = !item.IsCheckable && item.ResolveIconTemplate() is IconTemplate template ? RasterizeIcon(template) : null;

        if (item.SubMenu is Menu subMenu)
        {
            return new NativeMenuEntry
            {
                Kind = NativeMenuEntryKind.SubMenu,
                Text = text,
                AccessKeyIndex = accessKeyIndex,
                IsEnabled = item.IsEnabled,
                Icon = icon,
                Children = Read(subMenu),
                Item = item,
            };
        }

        bool enabled = item.IsEnabled && (item.CanClick?.Invoke() ?? true);
        KeyGesture? gesture = null;
        if (item.Command is Command command)
        {
            enabled = enabled && _router.CanExecute(command, _target, item.CommandData);
            gesture = ResolveGesture(command, item.CommandData);
        }

        return new NativeMenuEntry
        {
            Kind = NativeMenuEntryKind.Item,
            Text = text,
            AccessKeyIndex = accessKeyIndex,
            IsEnabled = enabled,
            IsCheckable = item.IsCheckable,
            IsChecked = item.IsCheckable && item.IsChecked,
            Gesture = gesture,
            Icon = icon,
            Item = item,
        };
    }

    private KeyGesture? ResolveGesture(Command command, object? data)
    {
        KeyGesture gesture;
        bool found = _gestureWindow != null
            ? InputMapResolver.TryGetEffectiveGesture(_gestureWindow, command, _target.OriginElement, data, out gesture)
            : InputMapResolver.TryGetApplicationGesture(command, data, out gesture);
        return found ? gesture : null;
    }

    private NativeMenuIcon? RasterizeIcon(IconTemplate template)
    {
        var size = IconTemplate.ResolveSize(_iconSizeDip, _iconScale);
        var bySize = _icons.GetOrCreateValue(template);
        if (!bySize.TryGetValue(size.Pixel, out var icon))
        {
            icon = DrawIcon(template, size, _iconScale);
            bySize[size.Pixel] = icon;
        }

        return icon;
    }

    private static NativeMenuIcon? DrawIcon(IconTemplate template, IconTemplateSize size, double scale)
    {
        if (!Application.IsRunning)
        {
            return null;
        }

        var element = template.Build(size);
        element.Width = size.Dip;
        element.Height = size.Dip;
        element.Measure(new Size(size.Dip, size.Dip));
        element.Arrange(new Rect(0, 0, size.Dip, size.Dip));

        int pixelSize = size.Pixel;
        var factory = Application.Current.GraphicsFactory;
        using var surface = factory.CreateSurface(
            RenderSurfaceDescriptor.CpuPixels(pixelSize, pixelSize, scale, premultiplied: true, nameof(NativeMenuSnapshot)));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            try
            {
                context.Clear(Color.Transparent);
                element.Render(context);
            }
            finally
            {
                context.EndFrame();
            }
        }

        var pixels = new byte[pixelSize * pixelSize * 4];
        if (!factory.TryReadPixels(surface, pixels, pixelSize * 4))
        {
            return null;
        }

        bool anyInk = false;
        bool glyph = true;
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset + 3] == 0)
            {
                continue;
            }

            anyInk = true;
            if (pixels[offset] != 0 || pixels[offset + 1] != 0 || pixels[offset + 2] != 0)
            {
                glyph = false;
                break;
            }
        }

        return anyInk ? new NativeMenuIcon(pixelSize, scale, pixels, glyph) : null;
    }

    private static List<NativeMenuEntry> RemoveStraySeparators(List<NativeMenuEntry> entries)
    {
        var result = new List<NativeMenuEntry>(entries.Count);
        foreach (var entry in entries)
        {
            bool separator = entry.Kind == NativeMenuEntryKind.Separator;
            if (separator && (result.Count == 0 || result[^1].Kind == NativeMenuEntryKind.Separator))
            {
                continue;
            }

            result.Add(entry);
        }

        if (result.Count > 0 && result[^1].Kind == NativeMenuEntryKind.Separator)
        {
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }
}
