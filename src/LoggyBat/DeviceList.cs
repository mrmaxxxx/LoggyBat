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

    // Fixed order that never depends on charge: type priority, then name, then dev-id.
    public static IReadOnlyList<(DeviceReading Row, string Label)> ForDisplay(IReadOnlyList<DeviceReading> rows)
    {
        var ordered = rows
            .OrderBy(r => Priority(r.Type))
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToList();

        return ordered
            .Select(r =>
            {
                var same = ordered.Where(o => string.Equals(o.Name, r.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                // Numbers only tell same-model devices apart right now; they may differ next session.
                return (r, same.Count > 1 ? $"{r.Name} ({same.IndexOf(r) + 1})" : r.Name);
            })
            .ToList();
    }

    // MOUSE and HEADSET are G HUB's deviceType strings seen on Max's PC. KEYBOARD and MICROPHONE are
    // expected strings not yet seen on a real device (the log records every type, see GHubClient);
    // any other type gets the neutral 🔋, so a wrong guess costs only the icon.
    public static string Icon(string type) => type switch
    {
        "MOUSE" => "🖱",
        "HEADSET" => "🎧",
        "KEYBOARD" => "⌨",
        "MICROPHONE" => "🎤",
        _ => "🔋",
    };

    // What the UI draws: U+FE0F asks for the colour emoji. 🖱 and ⌨ default to the narrow text form,
    // and drawing the wide emoji into that narrow box is the likely cause of the clipped mouse.
    public static string Glyph(string type) => Icon(type) + "️";

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

    public static string Percentage(DeviceReading r, DateTimeOffset now) =>
        IsFresh(r, now)
            ? r.Percent + "%" + (r.Charging ? " ⚡" : "")
            : "—";

    // Taskbar: at most two devices; a single "🔋 —" while none is known.
    public static IReadOnlyList<(string Type, string Percent)> PreviewItems(IReadOnlyList<DeviceReading> rows, DateTimeOffset now)
    {
        var shown = ForDisplay(rows)
            .Take(2)
            .Select(x => (x.Row.Type, Percentage(x.Row, now)))
            .ToList();

        return shown.Count > 0 ? shown : [("", "—")];
    }

    public static string PreviewText(IReadOnlyList<DeviceReading> rows, DateTimeOffset now) =>
        string.Join("   ", PreviewItems(rows, now).Select(x => $"{Icon(x.Type)} {x.Percent}"));

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

    // One entry per device: "G502 X PLUS: 92%" and the detail line under it.
    public static IReadOnlyList<(string Type, string Line, string Detail)> FlyoutRows(IReadOnlyList<DeviceReading> rows, DateTimeOffset now) =>
        ForDisplay(rows)
            .Select(x => (x.Row.Type, $"{x.Label}: {Percentage(x.Row, now)}", Status(x.Row, now)))
            .ToList();

    public static string FlyoutText(IReadOnlyList<DeviceReading> rows, string? clientProblem, DateTimeOffset now) =>
        FlyoutMessage(rows, clientProblem)
        ?? string.Join("\n\n", FlyoutRows(rows, now).Select(x => $"{Icon(x.Type)} {x.Line}\n{x.Detail}"));

    // Receive time only. Kept short: the flyout is narrow.
    private static string Status(DeviceReading r, DateTimeOffset now) =>
        IsFresh(r, now) ? $"Ответ G HUB: {r.ReceivedAt:HH:mm:ss}"
        : r.Problem is not null ? $"Нет данных: {r.Problem}"
        : "Нет данных: G HUB давно не отвечал";
}
