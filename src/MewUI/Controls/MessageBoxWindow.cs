namespace Aprillz.MewUI.Controls;

public enum MessageButtonRole { Accept, Reject, Destructive }

public sealed record MessageButton(string Text, MessageButtonRole Role);

public sealed class MessageBoxOptions
{
    public string Message { get; set; } = string.Empty;
    public string? Title { get; set; }
    public PromptIconKind Icon { get; set; } = PromptIconKind.Info;
    public IReadOnlyList<MessageButton>? Buttons { get; set; }
    public string? Detail { get; set; }
    public Window? Owner { get; set; }
    public List<MessageBoxCheckBox>? CheckBoxes { get; set; }
}

public sealed class MessageBoxCheckBox
{
    public string Text { get; set; } = string.Empty;
    public bool IsChecked { get; set; }

    public MessageBoxCheckBox(string text, bool initialChecked = false)
    {
        Text = text;
        IsChecked = initialChecked;
    }
}

public sealed class MessageBoxWindow : Window
{
    private readonly PromptIconKind _icon;
    private readonly string _message;
    private readonly string? _detail;
    private readonly IReadOnlyList<MessageButton> _buttons;
    private readonly List<MessageBoxCheckBox> _checkBoxes;

    private readonly List<(MessageBoxCheckBox proxy, CheckBox control)> _checkBoxControls = [];
    private bool _pendingRecenter;

    public bool? DialogResult { get; private set; }

    internal static IReadOnlyList<MessageButton> ButtonsOk =>
        [new(MewUIStrings.OK.Value, MessageButtonRole.Accept)];

    internal static IReadOnlyList<MessageButton> ButtonsOkCancel =>
        [new(MewUIStrings.OK.Value, MessageButtonRole.Accept), new(MewUIStrings.Cancel.Value, MessageButtonRole.Reject)];

    internal static IReadOnlyList<MessageButton> ButtonsYesNo =>
        [new(MewUIStrings.Yes.Value, MessageButtonRole.Accept), new(MewUIStrings.No.Value, MessageButtonRole.Destructive)];

    internal static IReadOnlyList<MessageButton> ButtonsYesNoCancel =>
        [new(MewUIStrings.Yes.Value, MessageButtonRole.Accept), new(MewUIStrings.No.Value, MessageButtonRole.Destructive), new(MewUIStrings.Cancel.Value, MessageButtonRole.Reject)];

    public MessageBoxWindow(
        string message,
        PromptIconKind icon = PromptIconKind.Info,
        IReadOnlyList<MessageButton>? buttons = null,
        string? detail = null,
        List<MessageBoxCheckBox>? checkBoxes = null,
        string? title = null)
    {
        _message = message;
        _icon = icon;
        _buttons = buttons ?? ButtonsOk;
        _detail = detail;
        _checkBoxes = checkBoxes ?? [];

        Title = title ?? IconToTitle(icon);
        Padding = new Thickness(16);
        StartupLocation = WindowStartupLocation.CenterOwner;
        IsAlertWindow = true;

        Closed += OnDialogClosed;
        PreviewKeyDown += OnPreviewKeyDown;
        BuildContent();
    }

    public void SetMaxHeightFromOwner(Window? owner)
    {
        double maxHeight = owner != null && owner.RenderSize.Height > 0
            ? owner.RenderSize.Height * 0.8
            : 600;
        WindowSize = WindowSize.FitContentSize(800, maxHeight);
    }

    private void BuildContent()
    {
        bool hasDetail = !string.IsNullOrEmpty(_detail);

        // Buttons — ordered by platform convention
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        AddButtons(buttonPanel);

        // Body
        var bodyPanel = new StackPanel
        {
            MinWidth = 160,
            Orientation = Orientation.Vertical,
            Spacing = 12
        };

        bodyPanel.Add(new TextBlock
        {
            Text = _message,
            TextWrapping = TextWrapping.Wrap,
        });

        // Detail toggle
        if (hasDetail)
        {
            var detailTextBox = new MultiLineTextBox
            {
                Text = _detail!,
                IsReadOnly = true,
                Wrap = true,
                FontSize = 12,
                IsVisible = false,
            };

            var detailAt = new AccessText();
            detailAt.SetRawText(MewUIStrings.ShowDetail.Value);
            var detailCheckBox = new CheckBox
            {
                Content = detailAt,
                Margin = new Thickness(0, 12, 0, 0)
            };
            detailCheckBox.CheckedChanged += isChecked =>
            {
                detailTextBox.IsVisible = isChecked == true;
                _pendingRecenter = true;
            };

            bodyPanel.Add(detailCheckBox);
            bodyPanel.Add(detailTextBox);

            ClientSizeChanged += _ =>
            {
                if (_pendingRecenter)
                {
                    _pendingRecenter = false;
                    CenterOnOwner();
                }
            };
        }

        var checkBoxPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 6
        };

        foreach (var proxy in _checkBoxes)
        {
            var at = new AccessText();
            at.SetRawText(proxy.Text);
            var cb = new CheckBox
            {
                Content = at,
                IsChecked = proxy.IsChecked
            };
            _checkBoxControls.Add((proxy, cb));
            checkBoxPanel.Add(cb);
        }

        checkBoxPanel.IsVisible = checkBoxPanel.Children.Count > 0;

        // Layout
        var iconControl = new PromptIcon
        {
            Kind = _icon,
            Width = 48,
            Height = 48,
            VerticalAlignment = VerticalAlignment.Top
        };
        DockPanel.SetDock(iconControl, Dock.Left);
        DockPanel.SetDock(checkBoxPanel, Dock.Bottom);
        DockPanel.SetDock(buttonPanel, Dock.Bottom);

        var root = new DockPanel
        {
            Spacing = 16
        };
        root.Add(buttonPanel);
        root.Add(checkBoxPanel);
        root.Add(iconControl);
        root.Add(bodyPanel);

        Content = root;
    }

    private void AddButtons(StackPanel panel)
    {
        // Sort by platform convention.
        // Windows: Accept → Destructive → Reject (left to right)
        // macOS:   Reject → Destructive → Accept (left to right, primary on right)
        var ordered = OperatingSystem.IsMacOS()
            ? _buttons.OrderBy(b => b.Role switch
            {
                MessageButtonRole.Reject => 0,
                MessageButtonRole.Destructive => 1,
                MessageButtonRole.Accept => 2,
                _ => 1,
            })
            : _buttons.OrderBy(b => b.Role switch
            {
                MessageButtonRole.Accept => 0,
                MessageButtonRole.Destructive => 1,
                MessageButtonRole.Reject => 2,
                _ => 1,
            });

        foreach (var button in ordered)
        {
            var result = RoleToResult(button.Role);
            var btn = new Button
            {
                MinWidth = 60,
                Content = new Label
                {
                    Text = button.Text,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            btn.Click += () => CloseWith(result);
            panel.Add(btn);
        }
    }

    private static string IconToTitle(PromptIconKind icon) => icon switch
    {
        PromptIconKind.Info => MewUIStrings.Information.Value,
        PromptIconKind.Warning => MewUIStrings.Warning.Value,
        PromptIconKind.Error => MewUIStrings.Error.Value,
        PromptIconKind.Question => MewUIStrings.Question.Value,
        PromptIconKind.Success => MewUIStrings.Success.Value,
        PromptIconKind.Shield => MewUIStrings.Shield.Value,
        PromptIconKind.Crash => MewUIStrings.Crash.Value,
        _ => string.Empty,
    };

    private static bool? RoleToResult(MessageButtonRole role) => role switch
    {
        MessageButtonRole.Accept => true,
        MessageButtonRole.Reject => false,
        MessageButtonRole.Destructive => null,
        _ => false,
    };

    private void CloseWith(bool? result)
    {
        DialogResult = result;
        foreach (var (proxy, control) in _checkBoxControls)
        {
            proxy.IsChecked = control.IsChecked == true;
        }

        Close();
    }

    private void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void OnDialogClosed()
    {
        // If closed without clicking a button (X button, Escape, etc.), treat as Reject.
        if (DialogResult == null && !_buttons.Any(b => b.Role == MessageButtonRole.Destructive))
        {
            // No destructive button exists — null is unambiguous "closed without choice".
            // If a reject button exists, use false; otherwise true (OK-only dialog).
            DialogResult = _buttons.Any(b => b.Role == MessageButtonRole.Reject) ? false : true;
        }
    }
}
