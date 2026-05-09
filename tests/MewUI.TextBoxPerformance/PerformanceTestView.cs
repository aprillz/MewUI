using System.Diagnostics;
using System.IO.Compression;

using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.TextBoxPerformance;

partial class PerformanceTestView : UserControl
{
    private TextBox _singleLineTextBox = null!;
    private MultiLineTextBox _multiLineTextBox = null!;
    private MultiLineTextBox _emojiTextBox = null!;
    private MultiLineTextBox _logOutput = null!;

    public PerformanceTestView()
    {
        Build();
    }

    protected override Element? OnBuild() =>
        new DockPanel()
            .Spacing(12)
            .Children(
                // Bottom: log
                new StackPanel()
                    .Vertical()
                    .Spacing(4)
                    .DockBottom()
                    .Children(
                        new Label().Text("Log").Bold(),
                        new MultiLineTextBox()
                            .Ref(out _logOutput)
                            .IsReadOnly()
                            .Wrap()
                            .FontSize(11)
                            .Height(160)
                    ),
                // Center: tabs
                new TabControl()
                    .Padding(12)
                    .TabItems(
                        new TabItem()
                            .Header("Single-line (TextBox)")
                            .Content(BuildSingleLineTab()),
                        new TabItem()
                            .Header("Multi-line (MultiLineTextBox)")
                            .Content(BuildMultiLineTab()),
                        new TabItem()
                            .Header("emoji (MultiLineTextBox)")
                            .Content(BuildEmojiTab())
                    )
            );

    private FrameworkElement BuildSingleLineTab() =>
        new DockPanel()
            .Children(
                new WrapPanel()
                    .Spacing(8)
                    .Padding(0, 8)
                    .DockTop()
                    .Children(
                        new Button().Content("Load singleine-test.zip").OnClick(OnLoadSingleLine),
                        new Button().Content("Clear").OnClick(OnClearSingleLine),
                        new Button().Content("Generate 10K chars").OnClick(() => GenerateSingleLine(10_000)),
                        new Button().Content("Generate 100K chars").OnClick(() => GenerateSingleLine(100_000)),
                        new Button().Content("Type Simulation (1000)").OnClick(OnTypeSimulationSingleLine)
                    ),
                new Label().Text("Place singleine-test.zip in Resources/").DockTop().Margin(0, 0, 0, 8),
                new TextBox()
                    .Ref(out _singleLineTextBox)
                    .Top()
            );

    private FrameworkElement BuildMultiLineTab() =>
        new DockPanel()
            .Children(
                new WrapPanel()
                    .Spacing(8)
                    .Padding(0, 8)
                    .DockTop()
                    .Children(
                        new Button().Content("Load multiline-test.zip").OnClick(OnLoadMultiLine),
                        new Button().Content("Clear").OnClick(OnClearMultiLine),
                        new Button().Content("Generate 1MB").OnClick(() => GenerateMultiLine(1_000_000)),
                        new Button().Content("Generate 10MB").OnClick(() => GenerateMultiLine(10_000_000)),
                        new Button().Content("Type Simulation (1000)").OnClick(OnTypeSimulationMultiLine)
                    ),
                new Label().Text("Place multiline-test.zip in Resources/").DockTop().Margin(0, 0, 0, 8),
                new MultiLineTextBox()
                    .Wrap()
                    .Ref(out _multiLineTextBox)
            );

    private FrameworkElement BuildEmojiTab() =>
        new DockPanel()
            .Children(
                new WrapPanel()
                    .Spacing(8)
                    .Padding(0, 8)
                    .DockTop()
                    .Children(
                        new Button().Content("Load emoji-test.zip").OnClick(OnLoadEmoji)
                    ),
                new Label().Text("Place emoji-test.zip in Resources/").DockTop().Margin(0, 0, 0, 8),
                new MultiLineTextBox()
                    .Wrap()
                    .FontFamily("Consolas")
                    .Ref(out _emojiTextBox)
            );

    private void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var line = $"[{timestamp}] {message}{Environment.NewLine}";
        _logOutput.AppendText(line, true);
    }

    private static string? LoadZipText(string zipName)
    {
        var zipPath = Path.Combine(AppContext.BaseDirectory, "Resources", zipName);
        if (!File.Exists(zipPath))
            zipPath = Path.Combine("Resources", zipName);
        if (!File.Exists(zipPath)) return null;

        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault();
        if (entry == null) return null;

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // --- SingleLine ---

    private void OnLoadSingleLine()
    {
        Log("Loading singleline-test.zip...");
        var sw = Stopwatch.StartNew();

        var text = LoadZipText("singleline-test.zip");
        if (text == null)
        {
            Log("ERROR: singleline-test.zip not found in Resources/");
            return;
        }

        var loadMs = sw.ElapsedMilliseconds;
        Log($"  Zip read: {loadMs}ms, length={text.Length:N0} chars");

        sw.Restart();
        _singleLineTextBox.Text = text;
        Log($"  TextBox.Text set: {sw.ElapsedMilliseconds}ms");
    }

    private void OnClearSingleLine()
    {
        var sw = Stopwatch.StartNew();
        _singleLineTextBox.Text = string.Empty;
        Log($"Clear TextBox: {sw.ElapsedMilliseconds}ms");
    }

    private void GenerateSingleLine(int length)
    {
        Log($"Generating {length:N0} chars for TextBox...");
        var sw = Stopwatch.StartNew();

        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789 ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var sb = new System.Text.StringBuilder(length);
        for (int i = 0; i < length; i++)
            sb.Append(chars[i % chars.Length]);

        var text = sb.ToString();
        Log($"  Generated: {sw.ElapsedMilliseconds}ms");

        sw.Restart();
        _singleLineTextBox.Text = text;
        Log($"  TextBox.Text set: {sw.ElapsedMilliseconds}ms");
    }

    private async void OnTypeSimulationSingleLine()
    {
        const int count = 1000;
        Log($"Type simulation: {count} chars → TextBox...");
        _singleLineTextBox.Text = string.Empty;
        _singleLineTextBox.Focus();

        const string sample = "abcdefghijklmnopqrstuvwxyz0123456789 ";
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < count; i++)
        {
            _singleLineTextBox.Text += sample[i % sample.Length];
            if (i % 100 == 0) await Task.Yield();
        }
        Log($"  Done: {sw.ElapsedMilliseconds}ms ({sw.ElapsedMilliseconds * 1000.0 / count:F1}µs/char)");
    }
    // --- Emoji --

    private void OnLoadEmoji()
    {
        Log("Loading emoji-test.zip...");
        var sw = Stopwatch.StartNew();

        var text = LoadZipText("emoji-test.zip");
        if (text == null)
        {
            Log("ERROR: emoji-test.zip not found in Resources/");
            return;
        }

        var loadMs = sw.ElapsedMilliseconds;
        var lineCount = 0;
        foreach (var c in text) { if (c == '\n') lineCount++; }
        Log($"  Zip read: {loadMs}ms, length={text.Length:N0} chars, lines={lineCount:N0}");

        sw.Restart();
        _emojiTextBox.Text = text;
        Log($"  MultiLineTextBox.Text set: {sw.ElapsedMilliseconds}ms");
    }


    // --- MultiLine ---

    private void OnLoadMultiLine()
    {
        Log("Loading multiline-test.zip...");
        var sw = Stopwatch.StartNew();

        var text = LoadZipText("multiline-test.zip");
        if (text == null)
        {
            Log("ERROR: multiline-test.zip not found in Resources/");
            return;
        }

        var loadMs = sw.ElapsedMilliseconds;
        var lineCount = 0;
        foreach (var c in text) { if (c == '\n') lineCount++; }
        Log($"  Zip read: {loadMs}ms, length={text.Length:N0} chars, lines={lineCount:N0}");

        sw.Restart();
        _multiLineTextBox.Text = text;
        Log($"  MultiLineTextBox.Text set: {sw.ElapsedMilliseconds}ms");
    }

    private void OnClearMultiLine()
    {
        var sw = Stopwatch.StartNew();
        _multiLineTextBox.Text = string.Empty;
        Log($"Clear MultiLineTextBox: {sw.ElapsedMilliseconds}ms");
    }

    private void GenerateMultiLine(int targetSize)
    {
        Log($"Generating ~{targetSize / 1_000_000}MB for MultiLineTextBox...");
        var sw = Stopwatch.StartNew();

        const string line = "The quick brown fox jumps over the lazy dog. 0123456789 ABCDEFGHIJKLMNOPQRSTUVWXYZ\n";
        var repeatCount = targetSize / line.Length;
        var sb = new System.Text.StringBuilder(targetSize + line.Length);
        for (int i = 0; i < repeatCount; i++)
            sb.Append(line);

        var text = sb.ToString();
        Log($"  Generated: {sw.ElapsedMilliseconds}ms, length={text.Length:N0} chars");

        sw.Restart();
        _multiLineTextBox.Text = text;
        Log($"  MultiLineTextBox.Text set: {sw.ElapsedMilliseconds}ms");
    }

    private async void OnTypeSimulationMultiLine()
    {
        const int count = 1000;
        Log($"Type simulation: {count} chars → MultiLineTextBox...");
        _multiLineTextBox.Text = string.Empty;
        _multiLineTextBox.Focus();

        const string sample = "abcdefghijklmnopqrstuvwxyz0123456789 ";
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < count; i++)
        {
            char c = (i % 80 == 79) ? '\n' : sample[i % sample.Length];
            _multiLineTextBox.Text += c;
            if (i % 100 == 0) await Task.Yield();
        }
        Log($"  Done: {sw.ElapsedMilliseconds}ms ({sw.ElapsedMilliseconds * 1000.0 / count:F1}µs/char)");
    }
}
