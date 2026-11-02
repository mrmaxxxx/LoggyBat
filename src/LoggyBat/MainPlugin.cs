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
    private Grid? _previewRoot;
    private StackPanel? _preview;
    private Grid? _flyoutRoot;
    private Border? _flyoutDivider;
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
    public override int FlyoutHeight => 320;
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

        Diag($"запуск: сохранённая автопроверка={_autoCheck.Enabled}; фоновая проверка {_autoCheck.Reason(DateTimeOffset.Now)}");
        if (Installed is { } installed)
        {
            UpdateChecker.Shared.Load(context.DataDirectory);
            UpdateChecker.Shared.Changed += OnUpdatesChanged;
            _updateLoop = UpdateLoopAsync(installed, _autoCheck, _stop.Token);
        }
    }

    // Diagnostics for the settings window and the automatic check: time (from Log), widget instance,
    // the automatic-check value and the policy's reason. No JSON, no G HUB replies.
    private string InstanceTag => DeviceList.ShortId($"{Context?.InstanceId}");

    private void Diag(string text) => Log.Write($"Настройки [{InstanceTag}]: {text}");

    // Every 10 minutes asks the policy whether this instance may start a check (see AutoCheckPolicy:
    // never while the settings window is open, never when switched off - also not at start); the
    // checker then asks GitHub only if its last answer is old enough.
    private async Task UpdateLoopAsync(Version installed, AutoCheckPolicy policy, CancellationToken ct)
    {
        string? lastReason = null;
        try
        {
            while (true)
            {
                string reason = policy.Reason(DateTimeOffset.Now);
                if (reason != lastReason)
                    Diag($"фоновая проверка {reason}");
                lastReason = reason;
                await UpdateChecker.Shared.AutoCheckAsync(policy, installed, ct, InstanceTag);
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

        _previewRoot = new Grid { Background = null };
        _previewRoot.Children.Add(_preview);
        // WidBar applies the Windows theme; the vector icons take their colour from it, so redraw.
        _previewRoot.ActualThemeChanged += (_, _) => UpdatePreview();

        UpdatePreview();
        UpdateActive();
        return _previewRoot;
    }

    public override UIElement? CreateFlyoutContent()
    {
        _ui ??= DispatcherQueue.GetForCurrentThread();
        _flyoutList = new StackPanel();
        _flyoutDivider = new Border { Height = 1, Margin = new Thickness(0, 10, 0, 0) };

        // "Шкалы": title, a divider, then the device rows; the Grid gives the list a bounded
        // height so a long list scrolls inside the fixed-size flyout.
        _flyoutRoot = new Grid { Padding = new Thickness(20, 16, 20, 8) };
        _flyoutRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _flyoutRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _flyoutRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _flyoutRoot.Children.Add(new TextBlock
        {
            Text = "LoggyBat",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        Grid.SetRow(_flyoutDivider, 1);
        _flyoutRoot.Children.Add(_flyoutDivider);

        var list = new ScrollViewer
        {
            Content = _flyoutList,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(list, 2);
        _flyoutRoot.Children.Add(list);
        _flyoutRoot.ActualThemeChanged += (_, _) => UpdateFlyout();

        UpdateFlyout();
        return _flyoutRoot;
    }

    private static TextBlock TextLine(string text) => new() { Text = text, FontSize = 16, TextWrapping = TextWrapping.Wrap };

    private static Brush DotBrush => new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0xE8, 0x11, 0x23));

    // Taskbar: "icon + percent" per device (no bars), the orange bolt while charging, the red dot on the right.
    private void UpdatePreview()
    {
        if (_preview is null || _previewRoot is null)
            return;

        var theme = _previewRoot.ActualTheme;
        _preview.Children.Clear();
        foreach (var (type, percent, charging) in DeviceList.PreviewItems(_ghub.Devices, _settings, DateTimeOffset.Now))
        {
            var icon = Icons.Device(DeviceList.IconKind(type), 16, theme);
            icon.Margin = new Thickness(_preview.Children.Count > 0 ? 14 : 0, 0, 5, 0);
            _preview.Children.Add(icon);
            _preview.Children.Add(new TextBlock
            {
                Text = percent,
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            if (charging)
            {
                var bolt = Icons.Bolt(16);
                bolt.Margin = new Thickness(3, 0, 0, 0);
                _preview.Children.Add(bolt);
            }
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
        if (_flyoutList is null || _flyoutRoot is null)
            return;

        var theme = _flyoutRoot.ActualTheme;
        if (_flyoutDivider is not null)
            _flyoutDivider.Background = new SolidColorBrush(Icons.Divider(theme));

        _flyoutList.Children.Clear();
        if (DeviceList.FlyoutMessage(_ghub.Devices, _ghub.Problem) is { } message)
        {
            var text = TextLine(message);
            text.Margin = new Thickness(0, 14, 0, 0);
            _flyoutList.Children.Add(text);
        }
        else
        {
            var rows = DeviceList.FlyoutRows(_ghub.Devices, DateTimeOffset.Now, _settings.Devices);
            for (int i = 0; i < rows.Count; i++)
            {
                var (type, name, percent, charging, level) = rows[i];
                _flyoutList.Children.Add(ScaleRow(type, name, percent, charging, level, theme, last: i == rows.Count - 1));
            }
        }

        AddUpdateNotice(_flyoutList);
    }

    // One "Шкалы" row: icon | name ... percent (+ bolt) / turquoise bar on a theme track; a divider below
    // every row but the last.
    private static FrameworkElement ScaleRow(string type, string name, string percent, bool charging, int level, ElementTheme theme, bool last)
    {
        var head = new Grid { ColumnSpacing = 8 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 15,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });
        var value = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        value.Children.Add(new TextBlock { Text = percent, FontSize = 20, VerticalAlignment = VerticalAlignment.Center });
        if (charging)
            value.Children.Add(Icons.Bolt(18));
        Grid.SetColumn(value, 1);
        head.Children.Add(value);

        // The bar: a theme track with a turquoise part of `level` percent (nothing when there is no charge).
        var bar = new Grid { Height = 8, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Icons.Track(theme)) };
        if (level > 0)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(level, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - level, GridUnitType.Star) });
            bar.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Icons.Bar) });
        }

        var right = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(head);
        right.Children.Add(bar);

        var row = new Grid { ColumnSpacing = 16, Padding = new Thickness(0, 14, 0, 14) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(Icons.Device(DeviceList.IconKind(type), 30, theme));
        Grid.SetColumn(right, 1);
        row.Children.Add(right);

        return new Border
        {
            Child = row,
            BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1),
            BorderBrush = new SolidColorBrush(Icons.Divider(theme))
        };
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
    }

    public UIElement? CreateSettingsContent(IWidgetSettingsContext ctx) =>
        CreateSettingsView(ctx);

    private UIElement CreateSettingsView(IWidgetSettingsContext ctx)
    {
        Diag($"окно настроек создано (CreateSettingsContent); автопроверка в SettingsJson окна={WidgetSettings.Parse(ctx.SettingsJson).AutoUpdateCheck}");
        return SettingsView.Create(
            ctx, _ghub.Devices, Installed,
            onOpened: () =>
            {
                _autoCheck?.OnSettingsOpened();
                Diag($"окно настроек Loaded; фоновая проверка {_autoCheck?.Reason(DateTimeOffset.Now)}");
            },
            onClosed: () =>
            {
                _autoCheck?.OnSettingsClosed(DateTimeOffset.Now);
                Diag($"окно настроек Unloaded; последняя полученная автопроверка={_autoCheck?.Enabled}; фоновая проверка {_autoCheck?.Reason(DateTimeOffset.Now)}");
            });
    }

    // Every edit in the settings window, and the original JSON again on Cancel: only the look
    // changes here, nothing is saved, requested or started.
    public override void OnSettingsDraftChanged(string json)
    {
        _settings = WidgetSettings.Parse(json);
        _autoCheck?.OnSettingsJson(json);
        var draft = WidgetSettings.Parse(json);
        Diag($"OnSettingsDraftChanged: автопроверка={draft.AutoUpdateCheck}, version>1={draft.FromNewerVersion}; фоновая проверка {_autoCheck?.Reason(DateTimeOffset.Now)}");
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
        _previewRoot = null;
        _flyoutList = null;
        _flyoutRoot = null;
        _flyoutDivider = null;
    }
}
