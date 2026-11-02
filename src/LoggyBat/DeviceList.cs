using System.Text.Json;

namespace LoggyBat;

// One device row of the widget. Id is G HUB's temporary dev-id, meaningful only
// within one connection. ReceivedAt is when G HUB returned a charge, not when the
// battery was measured (G HUB does not say that).
public sealed record DeviceReading(
    string Id,
    string Type,
    string Name,
    string Model,
    int? Percent,
    bool Charging,
    DateTimeOffset? ReceivedAt,
    string? Problem,
    DateTimeOffset? GoneSince); // set while the device is missing from G HUB's list

// The device list rules and every text the widget shows. Pure (no WinUI, no I/O),
// so tests/GHubProtocolCheck covers it.
public static class DeviceList
{
    // A charge older than this is not shown: G HUB stopped confirming it.
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(40);

    // A device that left G HUB's list stays as a "—" row this long (same connection
    // only), so a short dropout does not reshuffle the taskbar.
    public static readonly TimeSpan GoneGrace = TimeSpan.FromSeconds(60);

    public const string Connecting = "подключение к G HUB…";
    public const string GoneProblem = "исчезло из списка G HUB";

    // NOT_CONNECTED is what G HUB reported for a switched-off LIGHTSPEED mouse (2026-10-30).
    public static string InactiveProblem(string state) =>
        state == "NOT_CONNECTED" ? "не подключено (выключено?)" : "не активно в G HUB";

    // Builds the rows from a fresh G HUB list. Rows are matched only by dev-id and
    // only within the same connection; after a reconnect the list starts over,
    // so changed dev-ids never produce duplicates.
    public static IReadOnlyList<DeviceReading> Reconcile(
        IReadOnlyList<DeviceReading> rows,
        IReadOnlyList<GHubProtocol.Device> devices,
        bool newConnection,
        DateTimeOffset now)
    {
        if (newConnection)
            rows = [];

        var result = new List<DeviceReading>();
        foreach (var d in devices)
        {
            var old = rows.FirstOrDefault(r => r.Id == d.Id);
            result.Add(old is null
                ? new DeviceReading(d.Id, d.Type, d.Name, d.Model, null, false, null, null, null)
                : old with { Type = d.Type, Name = d.Name, Model = d.Model, GoneSince = null });
        }

        var newModels = devices
            .Where(d => rows.All(r => r.Id != d.Id))
            .Select(d => d.Model)
            .ToHashSet();

        foreach (var r in rows.Where(r => devices.All(d => d.Id != r.Id)))
        {
            var since = r.GoneSince ?? now;
            // A new dev-id of the same model is most likely this device coming back: don't show it twice.
            if (now - since < GoneGrace && !newModels.Contains(r.Model))
                result.Add(r with { Percent = null, Charging = false, Problem = GoneProblem, GoneSince = since });
        }

        return result;
    }

    public static DeviceReading ApplyBattery(DeviceReading row, JsonElement reply, DateTimeOffset now)
    {
        var battery = GHubProtocol.ParseBattery(reply, out string? problem);
        return battery is null
            ? row with { Percent = null, Charging = false, Problem = problem }
            : row with { Percent = battery.Percent, Charging = battery.Charging, ReceivedAt = now, Problem = null };
    }

    public static DeviceReading WithProblem(DeviceReading row, string problem) =>
        row with { Percent = null, Charging = false, Problem = problem };

    public const string NotFoundProblem = "сейчас нет в G HUB";

    // First 8 characters of a widget instance id, enough to tell instances apart in the log.
    public static string ShortId(string id) => id.Length == 0 ? "?" : id[..Math.Min(8, id.Length)];

    // Order that never depends on charge: the models the user picked, in the picked order, then the
    // rest by type priority, name and dev-id. A picked model missing from G HUB stays as a "—" row,
    // so it keeps its place and no other device moves into it. Units of one model stay separate rows.
    public static IReadOnlyList<(DeviceReading Row, string Label)> ForDisplay(
        IReadOnlyList<DeviceReading> rows, IReadOnlyList<ChosenDevice>? chosen = null)
    {
        chosen ??= [];
        var auto = rows
            .OrderBy(r => Priority(r.Type))
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToList();

        var ordered = new List<DeviceReading>();
        foreach (var c in chosen)
        {
            var units = auto.Where(r => r.Model == c.Model).ToList();
            ordered.AddRange(units.Count > 0
                ? units
                : [new DeviceReading("", c.Type, c.Name, c.Model, null, false, null, NotFoundProblem, null)]);
        }
        ordered.AddRange(auto.Where(r => chosen.All(c => c.Model != r.Model)));

        return ordered
            .Select(r =>
            {
                var same = ordered.Where(o => string.Equals(o.Name, r.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                // Numbers only tell same-model devices apart right now; they may differ next session.
                return (r, same.Count > 1 ? $"{r.Name} ({same.IndexOf(r) + 1})" : r.Name);
            })
            .ToList();
    }

    // Text form of a device type, for logs and tests (the UI draws vector icons, see IconKind).
    // MOUSE and HEADSET are G HUB's deviceType strings seen on Max's PC. KEYBOARD and MICROPHONE are
    // expected strings not yet seen on a real device (the log records every type, see GHubClient).
    public static string Icon(string type) => type switch
    {
        "MOUSE" => "🖱",
        "HEADSET" => "🎧",
        "KEYBOARD" => "⌨",
        "MICROPHONE" => "🎤",
        _ => "🔋",
    };

    // Which approved vector icon the UI draws (Icons.cs); any other type gets the adapter, so a wrong
    // guess about a type string costs only the icon.
    public static string IconKind(string type) => type switch
    {
        "MOUSE" => "mouse",
        "HEADSET" => "headset",
        "KEYBOARD" => "keyboard",
        "MICROPHONE" => "microphone",
        _ => "adapter",
    };

    private static int Priority(string type) => type switch
    {
        "MOUSE" => 0,
        "HEADSET" => 1,
        "KEYBOARD" => 2,
        "MICROPHONE" => 3,
        _ => 4,
    };

    public static bool IsFresh(DeviceReading r, DateTimeOffset now) =>
        r.Percent is not null &&
        r.Problem is null &&
        r.ReceivedAt is { } at &&
        now - at < StaleAfter;

    // "92%", or "—" when there is no fresh charge. Charging is shown separately (the orange bolt).
    public static string PercentText(DeviceReading r, DateTimeOffset now) =>
        IsFresh(r, now) ? r.Percent + "%" : "—";

    public static bool ShowsCharging(DeviceReading r, DateTimeOffset now) => IsFresh(r, now) && r.Charging;

    // Text form with the bolt as " ⚡", for logs and tests.
    public static string Percentage(DeviceReading r, DateTimeOffset now) =>
        PercentText(r, now) + (ShowsCharging(r, now) ? " ⚡" : "");

    // Taskbar: settings.Count devices, automatic or only the picked models; a single "—" with the
    // adapter icon while there is nothing to show.
    public static IReadOnlyList<(string Type, string Percent, bool Charging)> PreviewItems(
        IReadOnlyList<DeviceReading> rows, WidgetSettings settings, DateTimeOffset now)
    {
        var picked = settings.Devices;
        var shown = ForDisplay(rows, picked)
            .Where(x => picked.Count == 0 || picked.Any(c => c.Model == x.Row.Model))
            .Take(settings.Count)
            .Select(x => (x.Row.Type, PercentText(x.Row, now), ShowsCharging(x.Row, now)))
            .ToList();

        return shown.Count > 0 ? shown : [("", "—", false)];
    }

    public static IReadOnlyList<(string Type, string Percent, bool Charging)> PreviewItems(IReadOnlyList<DeviceReading> rows, DateTimeOffset now) =>
        PreviewItems(rows, WidgetSettings.Default, now);

    public static string PreviewText(IReadOnlyList<DeviceReading> rows, WidgetSettings settings, DateTimeOffset now) =>
        string.Join("   ", PreviewItems(rows, settings, now).Select(x => $"{Icon(x.Type)} {x.Percent}{(x.Charging ? " ⚡" : "")}"));

    public static string PreviewText(IReadOnlyList<DeviceReading> rows, DateTimeOffset now) =>
        PreviewText(rows, WidgetSettings.Default, now);

    // clientProblem: why the whole connection has no data, null when G HUB answers.
    // Returns the one-line state of the whole connection when there are no rows, otherwise null.
    public static string? FlyoutMessage(IReadOnlyList<DeviceReading> rows, string? clientProblem) =>
        rows.Count > 0 ? null
        : clientProblem switch
        {
            null => "Устройства с батареей в G HUB не найдены",
            Connecting => "Подключение к G HUB…",
            _ => $"Нет данных: {clientProblem}",
        };

    // One flyout row per device ("Шкалы"): name, "92%" or "—", the bolt, and the bar level 0..100
    // (0 when there is no fresh charge). The reasons for "—" stay in the log only.
    public static IReadOnlyList<(string Type, string Name, string Percent, bool Charging, int Level)> FlyoutRows(
        IReadOnlyList<DeviceReading> rows, DateTimeOffset now, IReadOnlyList<ChosenDevice>? chosen = null) =>
        ForDisplay(rows, chosen)
            .Select(x => (x.Row.Type, x.Label, PercentText(x.Row, now), ShowsCharging(x.Row, now),
                IsFresh(x.Row, now) ? Math.Clamp(x.Row.Percent ?? 0, 0, 100) : 0))
            .ToList();

    // Text form of the flyout, for tests: one line per device, or the state of the whole connection.
    public static string FlyoutText(
        IReadOnlyList<DeviceReading> rows, string? clientProblem, DateTimeOffset now, IReadOnlyList<ChosenDevice>? chosen = null) =>
        FlyoutMessage(rows, clientProblem)
        ?? string.Join("\n", FlyoutRows(rows, now, chosen).Select(x => $"{Icon(x.Type)} {x.Name}: {x.Percent}{(x.Charging ? " ⚡" : "")}"));
}
