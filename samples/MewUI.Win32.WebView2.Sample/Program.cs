using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

Thread.CurrentThread.SetApartmentState(ApartmentState.Unknown);
Thread.CurrentThread.SetApartmentState(ApartmentState.STA);

try
{
    AppDomain.CurrentDomain.UnhandledException += (_, e) => HandleDomainException(e);
    Application.DispatcherUnhandledException += e => HandleUIException(e);

    ObservableValue<int> newWindowMode = new ObservableValue<int>(0);
    TabControl tabControl = null!;
    var tabInfoMap = new Dictionary<TabItem, TabInfo>();

    Window window = null!;
    Application.Create()
        .UseAccent(Accent.Purple)
        .BuildMainWindow(() => new Window()
            .Ref(out window)
            .Resizable(900, 700)
            .Padding(4)
            .Title("MewUI.Win32.WebView2 Sample")
            .Content(
                new DockPanel()
                    .Spacing(4)
                    .Children(
                        new DockPanel()
                            .DockTop()
                            .Spacing(4)
                            .Children(
                                new Button()
                                    .Content("Back")
                                    .OnClick(() => GetSelectedWebView()?.GoBack()),
                                new Button()
                                    .Content("Forward")
                                    .OnClick(() => GetSelectedWebView()?.GoForward()),
                                new Button()
                                    .Content("Reload")
                                    .OnClick(() => GetSelectedWebView()?.Reload()),
                                new Button()
                                    .Content("New Tab")
                                    //.Content("➕")
                                    //.Apply(c => c.Width = c.MinHeight)
                                    //.Padding(0)
                                    .OnClick(() => AddTab(null)),

                                new StackPanel()
                                    .Horizontal()
                                    .Right()
                                    .CenterVertical()
                                    .Margin(8, 4)
                                    .Spacing(8)
                                    .Children(
                                        new Label()
                                            .CenterVertical()
                                            .Text("Popup Mode:"),

                                        new RadioButton()
                                            .Text("Popup")
                                            .CenterVertical()
                                            .BindIsChecked(newWindowMode, x => x == 0, x => (x, 0)),

                                        new RadioButton()
                                            .Text("New Tab")
                                            .CenterVertical()
                                            .BindIsChecked(newWindowMode, x => x == 1, x => (x, 1)),
                                        new RadioButton()
                                            .Text("New Tab(Deferral)")
                                            .CenterVertical()
                                            .BindIsChecked(newWindowMode, x => x == 2, x => (x, 2)))
                            ),

                        new TabControl()
                            .Ref(out tabControl)
                            .Padding(2)
                            .VerticalScroll(ScrollMode.Disabled)
                            .HorizontalScroll(ScrollMode.Disabled)
                            .OnSelectionChanged(_ => SyncAddressBarFromSelected())
                    )
            )
            .OnLoaded(() => AddTab(null))
            .WithTheme((t, c) =>
            {
                foreach (var info in tabInfoMap.Values)
                {
                    _ = ApplyDefaultHtmlThemeAsync(info.WebView, t.IsDark);
                }
            }))
        .Run();




    TabInfo? GetSelectedTabInfo()
    {
        var selected = tabControl.SelectedTab;
        if (selected == null)
        {
            return null;
        }

        return GetTabInfo(selected);
    }

    TabInfo? GetTabInfo(TabItem selected)
    {
        tabInfoMap.TryGetValue(selected, out var info);
        return info;
    }

    WebView2? GetSelectedWebView() => GetSelectedTabInfo()?.WebView;

    void NavigateFromAddressBar()
    {
        var selectedInfo = GetSelectedTabInfo();
        if (selectedInfo == null)
        {
            return;
        }

        var text = (selectedInfo.AddressBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // Keep per-tab address even if it's not a valid URI.
        selectedInfo.Address = text;

        if (!text.Contains("://", StringComparison.Ordinal) &&
            !text.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return;
        }

        selectedInfo.Address = uri.ToString();
        selectedInfo.WebView.Source = uri;
    }

    void SyncAddressBarFromSelected()
    {
        var selectedInfo = GetSelectedTabInfo();
        if (selectedInfo == null)
        {
            return;
        }

        selectedInfo.AddressBox.Text = selectedInfo.Address;
    }

    int IndexOfTab(TabItem tab)
    {
        for (int i = 0; i < tabControl.Tabs.Count; i++)
        {
            if (ReferenceEquals(tabControl.Tabs[i], tab))
            {
                return i;
            }
        }

        return -1;
    }



    static Task ApplyDefaultHtmlThemeAsync(WebView2 webView, bool isDark)
    {
        if (webView.Source?.AbsolutePath is null or "about:blank")
        {
            return Task.CompletedTask;
        }

        return webView.ExecuteScriptAsync($"window.mewuiSetTheme && window.mewuiSetTheme({(isDark ? "true" : "false")});");

    }

    string WelcomeHtml(bool isDark)
    {
        var theme = isDark ? "dark" : "light";

        const string html = """
                   <!doctype html>
                   <html data-theme="__THEME__">
                   <head>
                     <meta charset="utf-8" />
                     <style>
                       :root {
                         --bg: #ffffff;
                         --fg: #1e1e1e;
                         --card-bg: #ffffff;
                         --card-border: #dddddd;
                         --code-bg: #f4f4f4;
                       }
                       html[data-theme="dark"] {
                         --bg: #1e1e1e;
                         --fg: #e6e6e8;
                         --card-bg: #1a1a1b;
                         --card-border: #303032;
                         --code-bg: rgba(255,255,255,0.08);
                       }
                       body {
                         font-family: Segoe UI, sans-serif;
                         margin: 0;
                         padding: 16px;
                         background: var(--bg);
                         color: var(--fg);
                         transition: background-color 220ms ease, color 220ms ease;
                       }
                       h1 { margin: 0 0 8px 0; font-size: 18px; }
                       .card {
                         padding: 12px;
                         border: 1px solid var(--card-border);
                         border-radius: 8px;
                         background: var(--card-bg);
                         transition: background-color 220ms ease, border-color 220ms ease;
                       }
                       code {
                         background: var(--code-bg);
                         padding: 2px 4px;
                         border-radius: 4px;
                       }
                     </style>
                     <script>
                       window.mewuiSetTheme = function(isDark) {
                         document.documentElement.dataset.theme = isDark ? "dark" : "light";
                       };
                     </script>
                   </head>
                   <body>
                     <h1>MewUI Win32 WebView2</h1>
                     <div class="card">
                       Use the address bar to navigate. This WebView is hosted via an HWND overlay.
                       <div style="margin-top: 8px;">Try: <code>https://example.com</code></div>
                     </div>
                   </body>
                   </html>
                   """;

        return html.Replace("__THEME__", theme, StringComparison.Ordinal);
    }

    TabInfo AddTab(Uri? initialUri)
    {
        Label titleText = null!;
        var webView = new WebView2();
        var addressBox = new TextBox
        {
            Text = initialUri?.ToString() ?? "about:blank",
        };
        var info = new TabInfo
        {
            WebView = webView,
            Address = initialUri?.ToString() ?? "about:blank",
            AddressBox = addressBox,
        };

        webView.DocumentTitleChanged += e =>
        {
            var title = webView.DocumentTitle;

            if (title?.StartsWith("data:text/html;") == true)
            {
                title = null;
            }

            titleText.Text = title?.Length > 0 ? title.Length > 30 ? title[..30] + "..." : title : "Tab";
        };
        webView.SourceChanged += e =>
        {
            info.Address = webView.Source?.AbsoluteUri ?? string.Empty;

            info.AddressBox.Text = webView.Source?.AbsoluteUri ?? string.Empty;
        };

        webView.CoreWebView2InitializationCompleted += (e) =>
        {
            if (initialUri is not null)
            {
                webView.Source = new Uri(initialUri.AbsoluteUri);
            }
            else
            {
                webView.NavigateToString(WelcomeHtml(Application.Current.Theme.IsDark));
            }
        };

        webView.NewWindowRequested += async (e) =>
        {
            if (newWindowMode.Value == 1)
            {
                // Open in new tab instead of new window
                e.Handled = true;

                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
                {
                    AddTab(uri);
                }
            }
            else if (newWindowMode.Value == 2)
            {
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
                {
                    using var deferral = e.GetDeferral();
                    var info = AddTab(uri);

                    await info.WebView.EnsureCoreWebView2Async();  // 초기화 대기

                    e.NewWindow = info.WebView.CoreWebView2;  // 리다이렉트
                    e.Handled = true;

                    deferral.Complete();
                }
            }
            // If not checked, let the default behavior create a new window
        };

        TabItem tab = null!;
        new TabItem().Ref(out tab)
            .Header(
                new StackPanel()
                    .Horizontal()
                    .Children(
                        new Label()
                            .Ref(out titleText)
                            .Text(initialUri?.Host ?? "New Tab"),
                        new Button()
                            .Margin(8, 0, 0, 0)
                            .Content("❌")
                            .FontSize(10)
                            .MinHeight(0)
                            .Size(16, 16)
                            .Padding(new Thickness(0))
                            .CenterVertical()
                            .BorderThickness(0)
                            .OnClick(() =>
                            {
                                int index = IndexOfTab(tab);
                                if (index < 0)
                                {
                                    return;
                                }

                                var info = GetTabInfo(tab);
                                info?.WebView.Dispose();

                                tabInfoMap.Remove(tab);
                                tabControl.RemoveTabAt(index);
                                if (tabControl.Tabs.Count == 0)
                                {
                                    AddTab(null);
                                    tabControl.SelectedIndex = 0;
                                }
                            })
                    ))
            .Content(
                new DockPanel()
                    .Spacing(2)
                    .Children(
                        new DockPanel()
                            .Spacing(2)
                            .Children(
                                new Button()
                                    .Content("Go")
                                    .OnClick(() =>
                                    {
                                        tabControl.SelectedIndex = IndexOfTab(tab);
                                        NavigateFromAddressBar();
                                    }),
                                info.AddressBox
                                    .OnTextChanged(text => info.Address = text)
                                    .OnGotFocus(() => Application.Current.Dispatcher!.BeginInvoke(() => info.AddressBox.SelectAll()))
                                    .OnKeyDown(e =>
                                    {
                                        if (e.Key == Key.Enter)
                                        {
                                            if (ReferenceEquals(GetSelectedTabInfo(), info))
                                            {
                                                NavigateFromAddressBar();
                                            }
                                            else
                                            {
                                                var saved = tabControl.SelectedTab;
                                                tabControl.SelectedIndex = IndexOfTab(tab);
                                                NavigateFromAddressBar();
                                                if (saved != null)
                                                {
                                                    tabControl.SelectedIndex = IndexOfTab(saved);
                                                }
                                            }

                                            e.Handled = true;
                                        }
                                    })

                            )
                            .DockTop(),
                        webView
                    ));

        tabInfoMap[tab] = info;
        tabControl.AddTab(tab);
        tabControl.SelectedIndex = tabControl.Tabs.Count - 1;
        SyncAddressBarFromSelected();

        return info;
    }
}
catch (Exception ex)
{
    try
    {
        MessageBox.Show(ex.ToString(), "Fatal error", MessageBoxButtons.Ok, MessageBoxIcon.Error);
    }
    catch
    {
    }
}

static void HandleDomainException(UnhandledExceptionEventArgs e)
{
    try
    {
        if (e.ExceptionObject is Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Unhandled exception", MessageBoxButtons.Ok, MessageBoxIcon.Error);
        }
        else
        {
            MessageBox.Show(e.ExceptionObject?.ToString() ?? "Unknown error", "Unhandled exception", MessageBoxButtons.Ok, MessageBoxIcon.Error);
        }
    }
    catch
    {
    }
}

static void HandleUIException(DispatcherUnhandledExceptionEventArgs e)
{
    try
    {
        MessageBox.Show(e.Exception.ToString(), "Unhandled exception", MessageBoxButtons.Ok, MessageBoxIcon.Error);
    }
    catch
    {
    }

    e.Handled = true;
}

record TabInfo
{
    public required WebView2 WebView { get; init; }

    public required TextBox AddressBox { get; init; } = null!;

    public string Address { get; set; } = "about:blank";
}
