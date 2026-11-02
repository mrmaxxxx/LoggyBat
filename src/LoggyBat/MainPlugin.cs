using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidBar.SDK;

namespace LoggyBat;

public sealed class MainPlugin :
    WidgetPluginBase,
    IWidgetFlyoutLifecycle,
    IConfigurableWidgetPlugin
{
    private readonly GHubClient _ghub = new();
    private DispatcherQueue? _ui;
    private StackPanel? _preview;
    private StackPanel? _flyoutList;

    // What the widget shows: the saved settings, or the draft while the settings window is open.
    private WidgetSettings _settings = WidgetSettings.Default;

    private bool _flyoutVisible;
    private bool _disposed;

    public override string Id => "com.mrmaxxxx.loggybat";
    public override string Name => "LoggyBat";

    // 190 for two devices, as before; WidBar re-reads it after every settings change.
    public override int PreviewLogicalWidth => 20 + 85 * _settings.Count;
    public override int FlyoutWidth => 360;
    public override int FlyoutHeight => 220;
    public override WidgetFlyoutBackdrop FlyoutBackdrop =>
        WidgetFlyoutBackdrop.Acrylic;

    public override async Task InitializeAsync(IWidgetContext context)
    {
        _settings = WidgetSettings.Parse(context.SettingsJson);
        await base.InitializeAsync(context);
        context.PreviewVisibilityChanged += OnPreviewVisibilityChanged;
        _ghub.Changed += OnGHubChanged;
        UpdateActive();
        _ghub.Start();
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
        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI Emoji"),
        FontSize = 16,
        VerticalAlignment = VerticalAlignment.Center
    };

    // Same look as the former single line "🖱 92%   🎧 69%".
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
    }

    private void UpdateFlyout()
    {
        if (_flyoutList is null)
            return;

        _flyoutList.Children.Clear();
        _flyoutList.Spacing = _settings.Diagnostics ? 16 : 6;
        if (DeviceList.FlyoutMessage(_ghub.Devices, _ghub.Problem) is { } message)
        {
            _flyoutList.Children.Add(new TextBlock { Text = message, FontSize = 16, TextWrapping = TextWrapping.Wrap });
            return;
        }

        foreach (var (type, line, detail) in DeviceList.FlyoutRows(_ghub.Devices, DateTimeOffset.Now, _settings.Devices))
        {
            // Grid, not a horizontal StackPanel: a long name must wrap inside the flyout width.
            var head = new Grid { ColumnSpacing = 6 };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var name = new TextBlock { Text = line, FontSize = 16, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(name, 1);
            head.Children.Add(IconBlock(type));
            head.Children.Add(name);

            var entry = new StackPanel();
            entry.Children.Add(head);
            if (_settings.Diagnostics)
                entry.Children.Add(new TextBlock { Text = detail, FontSize = 16, TextWrapping = TextWrapping.Wrap });
            _flyoutList.Children.Add(entry);
        }
    }

    public UIElement? CreateSettingsContent(IWidgetSettingsContext ctx) =>
        SettingsView.Create(ctx, _ghub.Devices);

    // Every edit in the settings window, and the original JSON again on Cancel: only the look
    // changes here, nothing is saved or started.
    public override void OnSettingsDraftChanged(string json)
    {
        _settings = WidgetSettings.Parse(json);
        UpdateText();
        Context?.RequestPreviewRefresh();
    }

    // Comes from the G HUB loop thread; XAML may only be touched on the UI thread.
    private void OnGHubChanged() => _ui?.TryEnqueue(UpdateText);

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
        await _ghub.DisposeAsync();

        _preview = null;
        _flyoutList = null;
    }
}
