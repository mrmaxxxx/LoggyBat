using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using WidBar.SDK;

namespace LoggyBat;

public sealed class MainPlugin :
    WidgetPluginBase,
    IWidgetFlyoutLifecycle,
    IConfigurableWidgetPlugin
{
    // Version of the installed package; null when started outside the package (no update checks then).
    private static readonly Version? Installed = InstalledVersion();

    private readonly GHubClient _ghub = new();
    private readonly CancellationTokenSource _stop = new();
    private Task? _updateLoop;
    private AutoCheckPolicy? _autoCheck;
    private DispatcherQueue? _ui;
    private StackPanel? _preview;
    private StackPanel? _flyoutList;

    // What the widget shows: the saved settings, or the draft while the settings window is open.
    private WidgetSettings _settings = WidgetSettings.Default;

    private bool _flyoutVisible;
    private bool _disposed;

    public override string Id => "com.mrmaxxxx.loggybat";
    public override string Name => "LoggyBat";

    // 190 for two devices, as before, plus room for the red dot; WidBar re-reads it after every
    // settings change and after RequestPreviewRefresh.
    public override int PreviewLogicalWidth => 20 + 85 * _settings.Count + (ShowDot ? 16 : 0);
    public override int FlyoutWidth => 360;
    public override int FlyoutHeight => 260;
    public override WidgetFlyoutBackdrop FlyoutBackdrop =>
        WidgetFlyoutBackdrop.Acrylic;

    private static bool ShowDot => Installed is { } v && UpdateCheck.ShowsDot(UpdateChecker.Shared.Found, v);

    public override async Task InitializeAsync(IWidgetContext context)
    {
        _settings = WidgetSettings.Parse(context.SettingsJson);
        _autoCheck = new AutoCheckPolicy(context.SettingsJson);
        await base.InitializeAsync(context);
        context.PreviewVisibilityChanged += OnPreviewVisibilityChanged;
        _ghub.Changed += OnGHubChanged;
        UpdateActive();
        _ghub.Start();

        if (Installed is { } installed)
        {
            UpdateChecker.Shared.Load(context.DataDirectory);
            UpdateChecker.Shared.Changed += OnUpdatesChanged;
            _updateLoop = UpdateLoopAsync(installed, _autoCheck, _stop.Token);
        }
    }

    // Every 10 minutes asks the policy whether this instance may start a check (see AutoCheckPolicy:
    // never while the settings window is open, never when switched off - also not at start); the
    // checker then asks GitHub only if its last answer is old enough.
    private static async Task UpdateLoopAsync(Version installed, AutoCheckPolicy policy, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                await UpdateChecker.Shared.AutoCheckAsync(policy, installed, ct);
                await Task.Delay(TimeSpan.FromMinutes(10), ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Widget removed or WidBar closing.
        }
    }

    private static Version? InstalledVersion()
    {
        try
        {
            var v = Windows.ApplicationModel.Package.Current.Id.Version;
            return new Version(v.Major, v.Minor, v.Build, v.Revision);
        }
        catch (InvalidOperationException)
        {
            return null; // no package identity
        }
    }

    public override UIElement? CreatePreviewContent()
    {
        _ui ??= DispatcherQueue.GetForCurrentThread();
        _preview = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var root = new Grid { Background = null };
        root.Children.Add(_preview);

        UpdatePreview();
        UpdateActive();
        return root;
    }

    public override UIElement? CreateFlyoutContent()
    {
        _ui ??= DispatcherQueue.GetForCurrentThread();
        _flyoutList = new StackPanel { Spacing = 16 };
        UpdateFlyout();

        // Same look as the former StackPanel (padding 20, gap 12); the Grid gives the
        // list a bounded height so a long list scrolls inside the fixed-size flyout.
        var panel = new Grid
        {
            RowSpacing = 12,
            Padding = new Thickness(20)
        };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        panel.Children.Add(new TextBlock
        {
            Text = "LoggyBat",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        var list = new ScrollViewer
        {
            Content = _flyoutList,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(list, 1);
        panel.Children.Add(list);

        return panel;
    }

    // The icon is its own element in the emoji font, so its width is measured from the emoji itself
    // and the glyph is not clipped (the mouse was, when it shared one TextBlock with the text).
    private static TextBlock IconBlock(string type) => new()
    {
        Text = DeviceList.Glyph(type),
        FontFamily = new FontFamily("Segoe UI Emoji"),
        FontSize = 16,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static TextBlock TextLine(string text) => new() { Text = text, FontSize = 16, TextWrapping = TextWrapping.Wrap };

    private static Brush DotBrush => new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0xE8, 0x11, 0x23));

    // Same look as the former single line "🖱 92%   🎧 69%"; the red dot sits on the right.
    private void UpdatePreview()
    {
        if (_preview is null)
            return;

        _preview.Children.Clear();
        foreach (var (type, percent) in DeviceList.PreviewItems(_ghub.Devices, _settings, DateTimeOffset.Now))
        {
            var icon = IconBlock(type);
            icon.Margin = new Thickness(_preview.Children.Count > 0 ? 14 : 0, 0, 5, 0);
            _preview.Children.Add(icon);
            _preview.Children.Add(new TextBlock
            {
                Text = percent,
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        if (ShowDot)
        {
            var dot = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = DotBrush,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            AutomationProperties.SetName(dot, "Доступна новая версия LoggyBat");
            _preview.Children.Add(dot);
        }
    }

    private void UpdateFlyout()
    {
        if (_flyoutList is null)
            return;

        _flyoutList.Children.Clear();
        _flyoutList.Spacing = _settings.Diagnostics ? 16 : 6;
        if (DeviceList.FlyoutMessage(_ghub.Devices, _ghub.Problem) is { } message)
        {
            _flyoutList.Children.Add(TextLine(message));
        }
        else
        {
            foreach (var (type, line, detail) in DeviceList.FlyoutRows(_ghub.Devices, DateTimeOffset.Now, _settings.Devices))
            {
                // Grid, not a horizontal StackPanel: a long name must wrap inside the flyout width.
                var head = new Grid { ColumnSpacing = 6 };
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var name = TextLine(line);
                Grid.SetColumn(name, 1);
                head.Children.Add(IconBlock(type));
                head.Children.Add(name);

                var entry = new StackPanel();
                entry.Children.Add(head);
                if (_settings.Diagnostics)
                    entry.Children.Add(TextLine(detail));
                _flyoutList.Children.Add(entry);
            }
        }

        AddUpdateNotice(_flyoutList);
    }

    // A found newer release: message and a link to its page. Nothing is downloaded or installed.
    private void AddUpdateNotice(StackPanel list)
    {
        if (ShowDot && UpdateChecker.Shared.Found is { Url: { } url, Latest: { } latest })
        {
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
            head.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = DotBrush, VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(TextLine($"Доступна версия {latest} (у вас {Installed})"));

            var link = new HyperlinkButton { Content = "Открыть страницу релиза", Padding = new Thickness(0) };
            link.Click += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(new Uri(url));

            var notice = new StackPanel { Spacing = 2 };
            notice.Children.Add(head);
            notice.Children.Add(link);
            list.Children.Add(notice);
        }
        else if (_settings.Diagnostics && UpdateChecker.Shared.Last is { Status: UpdateStatus.Failed } failed)
        {
            list.Children.Add(TextLine($"Проверка обновлений: {failed.Text}"));
        }
    }

    public UIElement? CreateSettingsContent(IWidgetSettingsContext ctx) =>
        SettingsView.Create(
            ctx, _ghub.Devices, Installed,
            onOpened: () => _autoCheck?.OnSettingsOpened(),
            onClosed: () => _autoCheck?.OnSettingsClosed(DateTimeOffset.Now));

    // Every edit in the settings window, and the original JSON again on Cancel: only the look
    // changes here, nothing is saved, requested or started.
    public override void OnSettingsDraftChanged(string json)
    {
        _settings = WidgetSettings.Parse(json);
        _autoCheck?.OnSettingsJson(json);
        UpdateText();
        Context?.RequestPreviewRefresh();
    }

    // Comes from the G HUB loop thread; XAML may only be touched on the UI thread.
    private void OnGHubChanged() => _ui?.TryEnqueue(UpdateText);

    // A check finished (any instance, any thread): the dot may appear or go, so the width may change.
    private void OnUpdatesChanged() => _ui?.TryEnqueue(() =>
    {
        UpdateText();
        if (!_disposed)
            Context?.RequestPreviewRefresh();
    });

    private void UpdateText()
    {
        if (_disposed)
            return;

        UpdatePreview();
        UpdateFlyout();
    }

    private void UpdateActive()
    {
        if (!_disposed)
            _ghub.SetActive((Context?.IsPreviewVisible ?? true) || _flyoutVisible);
    }

    private void OnPreviewVisibilityChanged(object? sender, bool isVisible) =>
        UpdateActive();

    public void OnFlyoutShown()
    {
        _flyoutVisible = true;
        UpdateText();
        UpdateActive();
    }

    public void OnFlyoutHidden()
    {
        _flyoutVisible = false;
        UpdateActive();
    }

    public override async ValueTask DisposeAsync()
    {
        _disposed = true;

        if (Context is not null)
            Context.PreviewVisibilityChanged -= OnPreviewVisibilityChanged;

        _ghub.Changed -= OnGHubChanged;
        UpdateChecker.Shared.Changed -= OnUpdatesChanged;
        _stop.Cancel();
        if (_updateLoop is not null)
            await _updateLoop;
        await _ghub.DisposeAsync();
        _stop.Dispose();

        _preview = null;
        _flyoutList = null;
    }
}
