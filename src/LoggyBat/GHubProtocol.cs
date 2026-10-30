using System.Text.Json;

namespace LoggyBat;

// Pure parsing of G HUB replies, no I/O, so tests/GHubProtocolCheck can cover it.
// Message shapes were checked against lghub_agent on Max's PC on 2026-10-30.
public static class GHubProtocol
{
    // Model ids stay the same; G HUB's devNNNNNNNN ids change when it restarts.
    public const int MousePid = 16537;   // G502 X PLUS Wireless
    public const int HeadsetPid = 2746;  // PRO X Wireless Gaming Headset

    public sealed record Device(string Id, bool Active);

    public sealed record Battery(int Percent, bool Charging);

    public static string ResultCode(JsonElement reply) =>
        reply.TryGetProperty("result", out var result) &&
        result.ValueKind == JsonValueKind.Object
            ? Str(result, "code") ?? ""
            : "";

    // Finds a device by model pid in a /devices/list reply.
    public static Device? FindDevice(JsonElement listReply, int pid)
    {
        if (!listReply.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("deviceInfos", out var devices) ||
            devices.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var device in devices.EnumerateArray())
        {
            if (device.ValueKind == JsonValueKind.Object &&
                device.TryGetProperty("pid", out var p) &&
                p.ValueKind == JsonValueKind.Number &&
                p.TryGetInt32(out int value) &&
                value == pid &&
                Str(device, "id") is { Length: > 0 } id)
            {
                return new Device(id, Str(device, "state") == "ACTIVE");
            }
        }

        return null;
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
