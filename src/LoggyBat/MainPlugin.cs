using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidBar.SDK;

namespace LoggyBat;

public sealed class MainPlugin :
    WidgetPluginBase,
    IWidgetFlyoutLifecycle
{
    private readonly GHubClient _ghub = new();
    private DispatcherQueue? _ui;
    private TextBlock? _previewText;
    private TextBlock? _flyoutText;

    private bool _flyoutVisible;
    private bool _disposed;

    public override string Id => "com.mrmaxxxx.loggybat";
    public override string Name => "LoggyBat";

    public override int PreviewLogicalWidth => 190;
    public override int FlyoutWidth => 360;
    public override int FlyoutHeight => 220;
    public override WidgetFlyoutBackdrop FlyoutBackdrop =>
        WidgetFlyoutBackdrop.Acrylic;

    public override async Task InitializeAsync(IWidgetContext context)
    {
        await base.InitializeAsync(context);
        context.PreviewVisibilityChanged += OnPreviewVisibilityChanged;
        _ghub.Changed += OnGHubChanged;
        UpdateActive();
        _ghub.Start();
    }

    public override UIElement? CreatePreviewContent()
    {
        _ui ??= DispatcherQueue.GetForCurrentThread();
        _previewText = new TextBlock
        {
            Text = PreviewText(),
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var root = new Grid { Background = null };
        root.Children.Add(_previewText);

        UpdateActive();
        return root;
    }

    public override UIElement? CreateFlyoutContent()
    {
        _ui ??= DispatcherQueue.GetForCurrentThread();
        _flyoutText = new TextBlock
        {
            Text = FlyoutText(),
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap
        };

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
            Content = _flyoutText,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(list, 1);
        panel.Children.Add(list);

        return panel;
    }

    private string PreviewText() =>
        DeviceList.PreviewText(_ghub.Devices, DateTimeOffset.Now);

    private string FlyoutText() =>
        DeviceList.FlyoutText(_ghub.Devices, _ghub.Problem, DateTimeOffset.Now);

    // Comes from the G HUB loop thread; XAML may only be touched on the UI thread.
    private void OnGHubChanged() => _ui?.TryEnqueue(UpdateText);

    private void UpdateText()
    {
        if (_disposed)
            return;

        if (_previewText is not null)
            _previewText.Text = PreviewText();

        if (_flyoutText is not null)
            _flyoutText.Text = FlyoutText();
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

        _previewText = null;
        _flyoutText = null;
    }
}
