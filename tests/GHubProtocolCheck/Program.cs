using System.Text.Json;
using LoggyBat;
using Dev = LoggyBat.GHubProtocol.Device;

// Reply shapes trimmed from lghub_agent on Max's PC, 2026-10-30.
const string List = """
{"msgId":"1","verb":"GET","path":"/devices/list","result":{"code":"SUCCESS","what":""},
 "payload":{"deviceInfos":[
  {"id":"dev00000000","deviceType":"HEADSET","displayName":"PRO X WIRELESS","deviceModel":"pro_71_gaming_wireless_headset","state":"ACTIVE","capabilities":{"hasBatteryStatus":true}},
  {"id":"dev00000001","deviceType":"MOUSE","displayName":"G502 X PLUS","deviceModel":"g502x_plus","state":"ACTIVE","capabilities":{"hasBatteryStatus":true}},
  {"id":"dev00000002","deviceType":"CAMERA","displayName":"Logitech BRIO","deviceModel":"brio","state":"ACTIVE","capabilities":{"hasBatteryStatus":false}}]}}
""";
const string Charge92 = """{"msgId":"2","result":{"code":"SUCCESS","what":""},"payload":{"percentage":92,"charging":false}}""";
const string Charge69Charging = """{"msgId":"3","result":{"code":"SUCCESS"},"payload":{"percentage":69.4,"charging":true}}""";
// Anonymized from the 2026-10-30 capture (mouse switched off 23:01:35, on 23:01:45): serials, paths and
// container ids removed; only the fields the widget reads are kept.
const string ListMouseOff = """
{"msgId":"5","verb":"GET","path":"/devices/list","origin":"backend","result":{"code":"SUCCESS","what":""},
 "payload":{"deviceInfos":[
  {"id":"dev00000000","deviceType":"HEADSET","displayName":"PRO X WIRELESS","deviceModel":"pro_71_gaming_wireless_headset","state":"ACTIVE","connectionType":"USB","capabilities":{"hasBatteryStatus":true},"activeInterfaces":[{"type":"DEVIO","deviceType":"HEADSET"}]},
  {"id":"dev00000001","deviceType":"MOUSE","displayName":"G502 X PLUS","deviceModel":"g502x_plus","state":"NOT_CONNECTED","connectionType":"WIRELESS","capabilities":{"hasBatteryStatus":true},"activeInterfaces":[]}]}}
""";
const string MouseOffBattery = """{"msgId":"6","verb":"GET","path":"/battery/dev00000001/state","origin":"backend","result":{"code":"NO_SUCH_PATH","what":"message path '/battery/dev00000001/state' not found."}}""";
const string DeviceEvent = """{"msgId":"00000000-0000-0000-0000-000000000001","verb":"BROADCAST","path":"/devices/state/changed","origin":"backend","payload":{"@type":"type.googleapis.com/logi.protocol.devices.Device.Info","id":"dev00000001","pid":16537,"state":"NOT_CONNECTED"}}""";
const string BatteryEvent = """{"msgId":"00000000-0000-0000-0000-000000000002","verb":"BROADCAST","path":"/battery/state/changed","origin":"backend","payload":{"@type":"type.googleapis.com/logi.protocol.wireless.Battery","deviceId":"dev00000001","percentage":89,"charging":false}}""";
const string Hello = """{"msgId":"","verb":"OPTIONS","path":"/","origin":"backend"}""";
const string Off = """{"msgId":"4","result":{"code":"INVALID_ARG","what":"Battery data not available"}}""";

static JsonElement J(string json) => JsonDocument.Parse(json).RootElement;

static void Check(bool ok, string what)
{
    if (!ok)
        throw new Exception("FAILED: " + what);
    Console.WriteLine("ok: " + what);
}

static Dev Mouse(string id) => new(id, "MOUSE", "G502 X PLUS", "g502x_plus", "ACTIVE");
static Dev Headset(string id) => new(id, "HEADSET", "PRO X WIRELESS", "pro_71_gaming_wireless_headset", "ACTIVE");
static Dev Keyboard(string id) => new(id, "KEYBOARD", "G915", "g915", "ACTIVE");
static Dev Other(string id) => new(id, "SPEAKER", "Z407", "z407", "ACTIVE");

var now = new DateTimeOffset(2026, 10, 30, 21, 42, 22, TimeSpan.FromHours(3));

// One poll the way GHubClient.PollAsync does it: inactive devices get a reason, the rest a battery reply.
static IReadOnlyList<DeviceReading> Poll(IReadOnlyList<DeviceReading> rows, IReadOnlyList<Dev> devices, bool newConnection, DateTimeOffset at, string reply) =>
    DeviceList.Reconcile(rows, devices, newConnection, at)
        .Select(r =>
            r.GoneSince is not null ? r
            : devices.First(x => x.Id == r.Id) is { Active: false } dev ? DeviceList.WithProblem(r, DeviceList.InactiveProblem(dev.State))
            : DeviceList.ApplyBattery(r, J(reply), at))
        .ToList();

// --- Parsing ---
var listed = GHubProtocol.ListBatteryDevices(J(List))!;
Check(listed.Count == 2, "device without battery (camera) is not listed");
Check(listed[0] == Headset("dev00000000") && listed[1] == Mouse("dev00000001"), "type, name, model, state parsed");
Check(GHubProtocol.ListBatteryDevices(J(Off)) is null, "reply without device list -> null (list error)");
Check(GHubProtocol.ListBatteryDevices(J("""{"payload":{"deviceInfos":[]}}"""))!.Count == 0, "empty device list -> 0 devices");
Check(GHubProtocol.ParseBattery(J(Charge92), out var p1) == new GHubProtocol.Battery(92, false) && p1 is null, "92%, not charging");
Check(GHubProtocol.ParseBattery(J(Charge69Charging), out _) == new GHubProtocol.Battery(69, true), "69.4% -> 69, charging");
Check(GHubProtocol.ParseBattery(J(Off), out var p2) is null && p2 is not null, "switched-off device -> no charge, has reason");
Check(GHubProtocol.ParseBattery(J("""{"result":{"code":"SUCCESS"},"payload":{"percentage":150}}"""), out _) is null, "out-of-range percent rejected");

// --- 0, 1, 2, 3+ devices on the taskbar ---
Check(DeviceList.PreviewText([], now) == "🔋 —", "0 devices -> 🔋 —");
Check(DeviceList.PreviewText(Poll([], [Mouse("dev1")], true, now, Charge92), now) == "🖱 92%", "1 device");

var mine = DeviceList.Reconcile([], listed, true, now).ToList();
mine[0] = DeviceList.ApplyBattery(mine[0], J(Charge69Charging), now);
mine[1] = DeviceList.ApplyBattery(mine[1], J(Charge92), now);
Check(DeviceList.PreviewText(mine, now) == "🖱 92%   🎧 69% ⚡", "Max's mouse + headset look exactly as before");
Check(DeviceList.FlyoutText(mine, null, now) ==
    "🖱 G502 X PLUS: 92%\nОтвет G HUB: 21:42:22\n\n🎧 PRO X WIRELESS: 69% ⚡\nОтвет G HUB: 21:42:22", "flyout for mouse + headset");

var four = Poll([], [Other("dev0"), Keyboard("dev1"), Headset("dev2"), Mouse("dev3")], true, now, Charge92);
Check(DeviceList.PreviewText(four, now) == "🖱 92%   🎧 92%", "3+ devices: taskbar shows mouse, headset");
Check(DeviceList.ForDisplay(four).Select(x => x.Row.Type).SequenceEqual(["MOUSE", "HEADSET", "KEYBOARD", "SPEAKER"]), "flyout order: mouse, headset, keyboard, others");
Check(DeviceList.FlyoutText(four, null, now).Contains("⌨ G915: 92%") && DeviceList.FlyoutText(four, null, now).Contains("🔋 Z407: 92%"), "keyboard ⌨, unknown type 🔋");

// --- Two units of the same model ---
var twins = Poll([], [Mouse("dev5"), Mouse("dev4")], true, now, Charge92);
var twinLabels = DeviceList.ForDisplay(twins).Select(x => (x.Row.Id, x.Label)).ToList();
Check(twinLabels.SequenceEqual([("dev4", "G502 X PLUS (1)"), ("dev5", "G502 X PLUS (2)")]), "two same models: both kept, numbered, stable order");
var twinsUneven = twins.Select(r => r.Id == "dev5" ? r with { Percent = 10 } : r).ToList();
Check(DeviceList.ForDisplay(twinsUneven).Select(x => x.Row.Id).SequenceEqual(["dev4", "dev5"]), "charge does not change the order");

// --- Reconnect with new dev-ids: no duplicates ---
var before = Poll([], [Mouse("dev3"), Headset("dev4")], true, now, Charge92);
var after = Poll(before, [Mouse("dev0"), Headset("dev1")], newConnection: true, now.AddSeconds(30), Charge92);
Check(after.Count == 2 && after.All(r => r.GoneSince is null), "ids changed after reconnect -> still 2 rows, no ghosts");

// --- Disappearing and reappearing within one connection ---
var t0 = Poll([], [Mouse("dev1"), Headset("dev2")], true, now, Charge92);
var gone = Poll(t0, [Headset("dev2")], false, now.AddSeconds(10), Charge92);
var ghost = gone.Single(r => r.Id == "dev1");
Check(gone.Count == 2 && ghost.Problem == DeviceList.GoneProblem && ghost.Percent is null, "missing device: kept as '—' with 'исчезло из списка'");
Check(DeviceList.PreviewText(gone, now.AddSeconds(10)) == "🖱 —   🎧 92%", "missing mouse does not break the headset");
Check(Poll(gone, [Headset("dev2")], false, now.AddSeconds(75), Charge92).Count == 1, "missing device dropped after the 60 s grace");
var back = Poll(gone, [Mouse("dev1"), Headset("dev2")], false, now.AddSeconds(20), Charge92);
Check(back.Count == 2 && back.All(r => r.GoneSince is null && r.Percent == 92), "same id back -> row revived, no duplicate");
var backNewId = Poll(gone, [Mouse("dev7"), Headset("dev2")], false, now.AddSeconds(20), Charge92);
Check(backNewId.Count == 2 && backNewId.Any(r => r.Id == "dev7"), "back with a new id -> old row dropped, no duplicate");

// --- One device failing does not break the others ---
var rows = DeviceList.Reconcile([], [Mouse("dev1"), Headset("dev2")], true, now).ToList();
rows[0] = DeviceList.ApplyBattery(rows[0], J(Charge92), now);
rows[1] = DeviceList.ApplyBattery(rows[1], J(Off), now);
Check(DeviceList.PreviewText(rows, now) == "🖱 92%   🎧 —", "headset off: mouse still shown");
Check(DeviceList.FlyoutText(rows, null, now).Contains("Нет данных: устройство выключено?"), "present but no charge != gone");

// --- Freshness ---
Check(DeviceList.PreviewText(t0, now.AddSeconds(41)) == "🖱 —   🎧 —", "charge older than 40 s is not shown");

// --- Whole-connection states in the flyout ---
Check(DeviceList.FlyoutText([], DeviceList.Connecting, now) == "Подключение к G HUB…", "state: connecting");
Check(DeviceList.FlyoutText([], "G HUB не запущен", now) == "Нет данных: G HUB не запущен", "state: G HUB unavailable");
Check(DeviceList.FlyoutText([], null, now) == "Устройства с батареей в G HUB не найдены", "state: no battery devices");
Check(DeviceList.FlyoutText([], "ошибка списка устройств G HUB (FAILED)", now) == "Нет данных: ошибка списка устройств G HUB (FAILED)", "state: list error");

// --- G HUB change events (seen live 2026-10-30) ---
Check(GHubProtocol.IsChangeEvent(J(DeviceEvent)), "device state event wakes the loop");
Check(GHubProtocol.IsChangeEvent(J(BatteryEvent)), "battery event wakes the loop");
Check(!GHubProtocol.IsChangeEvent(J(Hello)), "connection hello is not a change event");
Check(!GHubProtocol.IsChangeEvent(J(List)) && !GHubProtocol.IsChangeEvent(J(Charge92)) && !GHubProtocol.IsChangeEvent(J(MouseOffBattery)), "replies to our GETs are not change events");

// --- Mouse switched off and on (sequence from the capture) ---
var off = GHubProtocol.ListBatteryDevices(J(ListMouseOff))!;
Check(off.Single(d => d.Id == "dev00000001") is { State: "NOT_CONNECTED", Active: false }, "switched-off mouse: state NOT_CONNECTED, not active");
Check(GHubProtocol.ParseBattery(J(MouseOffBattery), out var p3) is null && p3 == "G HUB ответил NO_SUCH_PATH", "battery GET for a switched-off mouse -> no charge, no crash");
var on1 = Poll([], listed, true, now, Charge92);
var off1 = Poll(on1, off, false, now.AddSeconds(2), Charge92);
Check(off1.Count == 2, "switched-off mouse stays one row, no duplicate");
Check(DeviceList.PreviewText(off1, now.AddSeconds(2)) == "🖱 —   🎧 92%", "mouse off: mouse —, headset still shown");
Check(DeviceList.FlyoutText(off1, null, now.AddSeconds(2)).Contains("🖱 G502 X PLUS: —\nНет данных: не подключено (выключено?)"), "mouse off: flyout says 'не подключено (выключено?)'");
Check(DeviceList.InactiveProblem("SOMETHING_ELSE") == "не активно в G HUB", "other inactive states keep the old text");
var on2 = Poll(off1, listed, false, now.AddSeconds(12), Charge92);
Check(DeviceList.PreviewText(on2, now.AddSeconds(12)) == "🖱 92%   🎧 92%", "mouse back on: charge shown again");
