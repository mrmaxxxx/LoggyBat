using System.Text.Json.Nodes;

namespace LoggyBat;

// A device the user picked for the taskbar. G HUB has no stable id for a unit, only the model,
// so the choice is per model; Type and Name are kept to draw it while it is missing from G HUB.
public sealed record ChosenDevice(string Model, string Type, string Name);

// Per-instance settings. WidBar stores them as an opaque JSON string (SettingsJson) and hands
// it back on start. Pure (no WinUI), so tests/GHubProtocolCheck covers it.
public sealed record WidgetSettings(
    int Count,                             // devices on the taskbar: 1..3
    IReadOnlyList<ChosenDevice> Devices,   // picked models in taskbar order; empty = automatic
    bool Diagnostics,                      // flyout shows the G HUB answer time and reasons
    bool AutoUpdateCheck,
    bool FromNewerVersion)                 // saved by a newer LoggyBat: shown, never overwritten
{
    public const int Version = 1;

    public static readonly WidgetSettings Default = new(2, [], false, true, false);

    // Anything unreadable falls back to the default for that field only; unknown fields are ignored here
    // and kept by ToJson.
    public static WidgetSettings Parse(string? json)
    {
        if (Root(json) is not { } root)
            return Default;

        var devices = new List<ChosenDevice>();
        if (root["devices"] is JsonArray list)
        {
            foreach (var item in list.OfType<JsonObject>())
            {
                if (Str(item, "model") is { Length: > 0 } model && devices.All(d => d.Model != model))
                    devices.Add(new ChosenDevice(model, Str(item, "type") ?? "", Str(item, "name") ?? model));
            }
        }

        return new WidgetSettings(
            Int(root, "count") is int count and >= 1 and <= 3 ? count : Default.Count,
            devices,
            Bool(root, "diagnostics") ?? Default.Diagnostics,
            Bool(root, "autoUpdateCheck") ?? Default.AutoUpdateCheck,
            Int(root, "version") > Version);
    }

    // Writes the known fields over the original JSON, so fields this version does not know survive.
    public string ToJson(string? originalJson)
    {
        var root = Root(originalJson) ?? new JsonObject();
        root["version"] = Version;
        root["count"] = Count;
        root["devices"] = new JsonArray(Devices
            .Select(d => (JsonNode)new JsonObject { ["model"] = d.Model, ["type"] = d.Type, ["name"] = d.Name })
            .ToArray());
        root["diagnostics"] = Diagnostics;
        root["autoUpdateCheck"] = AutoUpdateCheck;
        return root.ToJsonString();
    }

    private static JsonObject? Root(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string? Str(JsonObject o, string name) =>
        o[name] is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    private static int? Int(JsonObject o, string name) =>
        o[name] is JsonValue v && v.TryGetValue(out int i) ? i : null;

    private static bool? Bool(JsonObject o, string name) =>
        o[name] is JsonValue v && v.TryGetValue(out bool b) ? b : null;
}
