using System.Text.Json;

namespace LoggyBat;

// Pure parsing of G HUB replies, no I/O, so tests/GHubProtocolCheck can cover it.
// Message shapes were checked against lghub_agent on Max's PC on 2026-10-30.
public static class GHubProtocol
{
    // Id is G HUB's devNNNNNNNN: unique right now, but it changes when G HUB restarts.
    // Model is deviceModel, e.g. "g502x_plus": the same for every unit of a model.
    public sealed record Device(string Id, string Type, string Name, string Model, bool Active);

    public sealed record Battery(int Percent, bool Charging);

    public static string ResultCode(JsonElement reply) =>
        reply.TryGetProperty("result", out var result) &&
        result.ValueKind == JsonValueKind.Object
            ? Str(result, "code") ?? ""
            : "";

    // All devices with capabilities.hasBatteryStatus = true, in G HUB order;
    // null when the /devices/list reply has no device array at all.
    public static IReadOnlyList<Device>? ListBatteryDevices(JsonElement listReply)
    {
        if (!listReply.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("deviceInfos", out var devices) ||
            devices.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var result = new List<Device>();
        foreach (var device in devices.EnumerateArray())
        {
            if (device.ValueKind == JsonValueKind.Object &&
                device.TryGetProperty("capabilities", out var caps) &&
                caps.ValueKind == JsonValueKind.Object &&
                caps.TryGetProperty("hasBatteryStatus", out var hasBattery) &&
                hasBattery.ValueKind == JsonValueKind.True &&
                Str(device, "id") is { Length: > 0 } id)
            {
                string name =
                    Str(device, "displayName") is { Length: > 0 } display ? display
                    : Str(device, "extendedDisplayName") is { Length: > 0 } full ? full
                    : "Устройство Logitech";

                result.Add(new Device(
                    id,
                    (Str(device, "deviceType") ?? "").ToUpperInvariant(),
                    name,
                    Str(device, "deviceModel") is { Length: > 0 } model ? model : name,
                    Str(device, "state") == "ACTIVE"));
            }
        }

        return result;
    }

    // Parses a /battery/<id>/state reply; on failure returns null and a reason for the flyout.
    public static Battery? ParseBattery(JsonElement reply, out string? problem)
    {
        string code = ResultCode(reply);
        if (code != "SUCCESS")
        {
            // Seen for a headset that is switched off: INVALID_ARG "Battery data not available".
            problem = code == "INVALID_ARG"
                ? "устройство выключено?"
                : $"G HUB ответил {(code.Length > 0 ? code : "без кода")}";
            return null;
        }

        if (reply.TryGetProperty("payload", out var payload) &&
            payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("percentage", out var pct) &&
            pct.ValueKind == JsonValueKind.Number &&
            pct.TryGetDouble(out double percent) &&
            percent is >= 0 and <= 100)
        {
            bool charging = payload.TryGetProperty("charging", out var c) &&
                c.ValueKind == JsonValueKind.True;

            problem = null;
            return new Battery((int)Math.Round(percent), charging);
        }

        problem = "непонятный ответ G HUB";
        return null;
    }

    private static string? Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
