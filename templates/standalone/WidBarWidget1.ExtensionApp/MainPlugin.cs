using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidBar.SDK;

namespace WidBarWidget1.ExtensionApp;

public sealed class MainPlugin :
    WidgetPluginBase,
    IWidgetFlyoutLifecycle
{
    // A charge older than this is not shown: G HUB stopped confirming it.
    private static readonly TimeSpan StaleAfter = GHubClient.PollInterval * 4;

    private readonly GHubClient _ghub = new();
    private DispatcherQueue? _ui;
    private TextBlock? _previewText;
    private TextBlock? _flyoutText;

    private bool _flyoutVisible;
    private bool _disposed;

    public override string Id => "com.example.mywidget";
    public override string Name => "Logitech Battery";

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

        var panel = new StackPanel
        {
            Spacing = 12,
            Padding = new Thickness(20)
        };

        panel.Children.Add(new TextBlock
        {
            Text = "Logitech Battery",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        panel.Children.Add(_flyoutText);

        return panel;
    }

    private static bool IsFresh(DeviceReading r) =>
        r.Percent is not null &&
        r.Problem is null &&
        r.ReceivedAt is { } at &&
        DateTimeOffset.Now - at < StaleAfter;

    private static string Percentage(DeviceReading r) =>
        IsFresh(r)
            ? r.Percent + "%" + (r.Charging ? " ⚡" : "")
            : "—";

    // Receive time only: G HUB does not report when the battery was measured.
    // Kept short: the flyout has a fixed height.
    private static string Status(DeviceReading r) =>
        IsFresh(r) ? $"Ответ G HUB: {r.ReceivedAt:HH:mm:ss}"
        : r.Problem is not null ? $"Нет данных: {r.Problem}"
        : "Нет данных: G HUB давно не отвечал";

    private string PreviewText() =>
        $"🖱 {Percentage(_ghub.Mouse)}   🎧 {Percentage(_ghub.Headset)}";

    private string FlyoutText() =>
        $"🖱 G502 X PLUS: {Percentage(_ghub.Mouse)}\n" +
        $"{Status(_ghub.Mouse)}\n\n" +
        $"🎧 PRO X Wireless: {Percentage(_ghub.Headset)}\n" +
        $"{Status(_ghub.Headset)}";

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
