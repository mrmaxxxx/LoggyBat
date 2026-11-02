using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WidBar.SDK;

namespace LoggyBat;

// The settings window content. WidBar hosts it with its own Save/Cancel: every change here goes to
// ctx.SaveSettings (a draft until Save; Cancel hands the original JSON back to OnSettingsDraftChanged).
internal sealed class SettingsView
{
    private sealed class Entry(ChosenDevice device, int units, bool picked)
    {
        public ChosenDevice Device { get; } = device;
        public int Units { get; } = units;     // how many units of this model G HUB lists right now
        public bool Picked { get; set; } = picked;
    }

    private readonly IWidgetSettingsContext _ctx;
    private readonly string? _originalJson;
    private readonly List<Entry> _entries = [];
    private readonly StackPanel _deviceRows = new() { Spacing = 4 };
    private WidgetSettings _settings;

    private SettingsView(IWidgetSettingsContext ctx, IReadOnlyList<DeviceReading> current)
    {
        _ctx = ctx;
        _originalJson = ctx.SettingsJson;
        _settings = WidgetSettings.Parse(_originalJson);

        int Units(string model) => current.Count(r => r.Model == model);
        foreach (var d in _settings.Devices)
            _entries.Add(new Entry(d, Units(d.Model), true));
        foreach (var (row, _) in DeviceList.ForDisplay(current))
        {
            if (_entries.All(e => e.Device.Model != row.Model))
                _entries.Add(new Entry(new ChosenDevice(row.Model, row.Type, row.Name), Units(row.Model), false));
        }
    }

    public static UIElement Create(IWidgetSettingsContext ctx, IReadOnlyList<DeviceReading> current) =>
        new SettingsView(ctx, current).Build();

    private UIElement Build()
    {
        var panel = new StackPanel { Spacing = 12 };

        panel.Children.Add(Header("Панель задач"));
        var count = new RadioButtons { Header = "Сколько устройств показывать", MaxColumns = 3 };
        foreach (var n in new[] { "1", "2", "3" })
            count.Items.Add(n);
        count.SelectedIndex = _settings.Count - 1;
        count.SelectionChanged += (_, _) =>
        {
            if (count.SelectedIndex >= 0)
                Save(_settings with { Count = count.SelectedIndex + 1 });
        };
        panel.Children.Add(count);

        panel.Children.Add(Text("Устройства и порядок на панели"));
        FillDeviceRows();
        panel.Children.Add(_deviceRows);
        panel.Children.Add(Note(
            "Если ничего не отмечено, устройства выбираются автоматически: мышь, гарнитура, клавиатура, " +
            "микрофон, остальные. Отмеченное устройство не заменяется другим, пока его нет в G HUB: " +
            "на его месте будет «—».\n" +
            "Устройства запоминаются по модели: G HUB не даёт постоянного номера отдельного устройства. " +
            "Одинаковые устройства (например, две одинаковые мыши) нельзя выбрать по отдельности — они " +
            "отмечаются и переставляются вместе, а на панели и в окне каждое показывается отдельно, " +
            "со своим зарядом."));

        panel.Children.Add(Header("Окно виджета"));
        var diagnostics = new ToggleSwitch
        {
            Header = "Диагностика: время ответа G HUB и причина «—» под каждым устройством",
            IsOn = _settings.Diagnostics
        };
        diagnostics.Toggled += (_, _) => Save(_settings with { Diagnostics = diagnostics.IsOn });
        panel.Children.Add(diagnostics);

        var root = new StackPanel { Spacing = 16 };
        if (_settings.FromNewerVersion)
        {
            root.Children.Add(Note(
                "Эти настройки сохранены более новой версией LoggyBat. Чтобы они не потерялись, здесь они " +
                "только показываются; изменить их можно в новой версии."));
        }

        // A disabled ContentControl disables every control inside it.
        root.Children.Add(new ContentControl
        {
            Content = panel,
            IsEnabled = !_settings.FromNewerVersion,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        });

        return new ScrollViewer { Content = root, Padding = new Thickness(0, 0, 12, 0) };
    }

    private void FillDeviceRows()
    {
        _deviceRows.Children.Clear();
        if (_entries.Count == 0)
        {
            _deviceRows.Children.Add(Note("Устройства появятся здесь, когда G HUB их найдёт."));
            return;
        }

        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            int index = i;

            string label = entry.Device.Name
                + (entry.Units > 1 ? $" — {entry.Units} одинаковых, выбираются вместе" : "")
                + (entry.Units == 0 ? " — сейчас нет в G HUB" : "");
            var content = new Grid { ColumnSpacing = 6 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var name = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(name, 1);
            content.Children.Add(new TextBlock
            {
                Text = DeviceList.Glyph(entry.Device.Type),
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI Emoji")
            });
            content.Children.Add(name);

            var check = new CheckBox { Content = content, IsChecked = entry.Picked };
            AutomationProperties.SetName(check, entry.Device.Name);
            check.Click += (_, _) =>
            {
                entry.Picked = check.IsChecked == true;
                SaveDevices();
            };

            var row = new Grid { ColumnSpacing = 4 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(check);
            AddMoveButton(row, 1, "", "Выше", index > 0, () => Move(index, -1));
            AddMoveButton(row, 2, "", "Ниже", index < _entries.Count - 1, () => Move(index, +1));
            _deviceRows.Children.Add(row);
        }
    }

    private static void AddMoveButton(Grid row, int column, string glyph, string name, bool enabled, Action move)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 12 },
            IsEnabled = enabled,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(button, name);
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => move();
        Grid.SetColumn(button, column);
        row.Children.Add(button);
    }

    private void Move(int index, int delta)
    {
        (_entries[index], _entries[index + delta]) = (_entries[index + delta], _entries[index]);
        FillDeviceRows();
        SaveDevices();
    }

    private void SaveDevices() =>
        Save(_settings with { Devices = _entries.Where(e => e.Picked).Select(e => e.Device).ToList() });

    private void Save(WidgetSettings settings)
    {
        if (settings.FromNewerVersion)
            return; // never overwrite settings of a newer version

        _settings = settings;
        _ctx.SaveSettings(settings.ToJson(_originalJson));
        _ctx.RequestPreviewRefresh();
    }

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        FontSize = 16,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
    };

    private static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
}
