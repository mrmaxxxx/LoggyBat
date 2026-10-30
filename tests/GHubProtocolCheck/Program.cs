using System.Text.Json;
using LoggyBat;

// Replies trimmed from lghub_agent on Max's PC, 2026-10-30.
const string List = """
{"msgId":"1","verb":"GET","path":"/devices/list","result":{"code":"SUCCESS","what":""},
 "payload":{"deviceInfos":[
  {"id":"dev00000000","pid":2746,"state":"ACTIVE","deviceType":"HEADSET"},
  {"id":"dev00000001","pid":16537,"state":"ACTIVE","deviceType":"MOUSE"},
  {"id":"dev00000002","pid":2142,"state":"ACTIVE","deviceType":"CAMERA"}]}}
""";
const string Charge = """
{"msgId":"2","path":"/battery/dev00000001/state","result":{"code":"SUCCESS","what":""},
 "payload":{"deviceId":"dev00000001","percentage":92,"charging":false}}
""";
const string Charging = """
{"msgId":"3","result":{"code":"SUCCESS"},"payload":{"percentage":69.6,"charging":true}}
""";
const string Off = """
{"msgId":"4","result":{"code":"INVALID_ARG","what":"Battery data not available"}}
""";

static JsonElement J(string json) => JsonDocument.Parse(json).RootElement;

static void Check(bool ok, string what)
{
    if (!ok)
        throw new Exception("FAILED: " + what);
    Console.WriteLine("ok: " + what);
}

Check(GHubProtocol.ResultCode(J(List)) == "SUCCESS", "list result code");
Check(GHubProtocol.FindDevice(J(List), GHubProtocol.MousePid) == new GHubProtocol.Device("dev00000001", true), "mouse found by pid");
Check(GHubProtocol.FindDevice(J(List), GHubProtocol.HeadsetPid) == new GHubProtocol.Device("dev00000000", true), "headset found by pid");
Check(GHubProtocol.FindDevice(J(List), 1) is null, "unknown pid -> null");
Check(GHubProtocol.FindDevice(J(Off), GHubProtocol.MousePid) is null, "reply without list -> null");

Check(GHubProtocol.ParseBattery(J(Charge), out var p1) == new GHubProtocol.Battery(92, false) && p1 is null, "92%, not charging");
Check(GHubProtocol.ParseBattery(J(Charging), out _) == new GHubProtocol.Battery(70, true), "69.6% rounds to 70, charging");
Check(GHubProtocol.ParseBattery(J(Off), out var p2) is null && p2 is not null, "switched-off device -> no charge, has reason");
Check(GHubProtocol.ParseBattery(J("""{"result":{"code":"SUCCESS"},"payload":{"percentage":150}}"""), out _) is null, "out-of-range percent rejected");
Check(GHubProtocol.ParseBattery(J("""{"result":{"code":"SUCCESS"},"payload":{}}"""), out _) is null, "missing percent rejected");
