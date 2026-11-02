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

// --- Icons ---
Check(DeviceList.Icon("MICROPHONE") == "🎤" && DeviceList.Icon("SOMETHING_NEW") == "🔋" && DeviceList.Icon("") == "🔋", "microphone 🎤, unknown type 🔋");
Check(DeviceList.Glyph("MOUSE") == "🖱️" && DeviceList.Glyph("HEADSET") == "🎧️", "UI glyph asks for the emoji form (U+FE0F)");
Check(DeviceList.PreviewItems([], now).SequenceEqual([("", "—")]), "no devices: one neutral item");
var mic = Poll([], [Other("dev0"), new Dev("dev1", "MICROPHONE", "Mic", "mic", "ACTIVE")], true, now, Charge92);
Check(DeviceList.ForDisplay(mic).Select(x => x.Row.Type).SequenceEqual(["MICROPHONE", "SPEAKER"]), "microphone sorts before unknown types");

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

// --- Settings: defaults, damaged input, unknown and newer fields ---
static WidgetSettings S(string json) => WidgetSettings.Parse(json);
Check(S("") == WidgetSettings.Default && S("not json") == WidgetSettings.Default && S("[1,2]") == WidgetSettings.Default, "no/damaged settings -> defaults");
Check(WidgetSettings.Default is { Count: 2, Devices.Count: 0, Diagnostics: false, AutoUpdateCheck: true, FromNewerVersion: false }, "clean install: 2 devices, automatic, diagnostics off, update check on");
Check(S("""{"count":7,"diagnostics":"yes","autoUpdateCheck":false}""") is { Count: 2, Diagnostics: false, AutoUpdateCheck: false }, "bad field -> default for that field only");
Check(S("""{"count":3,"devices":[{"model":"g502x_plus","type":"MOUSE","name":"G502 X PLUS"},{"model":"g502x_plus"},{"type":"X"},5]}""") is { Count: 3, Devices: [{ Model: "g502x_plus", Type: "MOUSE" }] }, "devices: duplicates and entries without a model skipped");
var withExtra = """{"count":1,"futureOption":{"a":1},"version":1}""";
var saved = S(withExtra) with { Count = 3 };
Check(saved.ToJson(withExtra).Contains("\"futureOption\":{\"a\":1}") && S(saved.ToJson(withExtra)).Count == 3, "unknown fields survive a save");
Check(S("""{"version":2,"count":3,"newThing":true}""") is { FromNewerVersion: true, Count: 3 }, "settings of a newer version are recognised (shown, not overwritten)");
Check(S(WidgetSettings.Default.ToJson(null)) is { Count: 2, Devices.Count: 0, Diagnostics: false, AutoUpdateCheck: true, FromNewerVersion: false }, "default settings round-trip");

// --- Picked devices on the taskbar ---
var pickMouse = WidgetSettings.Default with { Count = 1, Devices = [new ChosenDevice("g502x_plus", "MOUSE", "G502 X PLUS")] };
Check(DeviceList.PreviewText(four, pickMouse, now) == "🖱 92%", "count 1, mouse picked");
var pickHeadsetFirst = WidgetSettings.Default with { Devices = [new ChosenDevice("pro_71_gaming_wireless_headset", "HEADSET", "PRO X WIRELESS"), new ChosenDevice("g915", "KEYBOARD", "G915")] };
Check(DeviceList.PreviewText(four, pickHeadsetFirst, now) == "🎧 92%   ⌨ 92%", "picked order is kept, mouse not picked -> not shown");
var headsetOnly = Poll([], [Mouse("dev1"), Keyboard("dev2")], true, now, Charge92);
Check(DeviceList.PreviewText(headsetOnly, pickHeadsetFirst, now) == "🎧 —   ⌨ 92%", "picked device missing -> '—' in its place, nothing moves in");
Check(DeviceList.FlyoutText(headsetOnly, null, now).Contains("G502 X PLUS") && DeviceList.FlyoutRows(headsetOnly, now, pickHeadsetFirst.Devices)[0].Line == "PRO X WIRELESS: —", "flyout: missing picked device first as '—', others still listed");
Check(DeviceList.PreviewText([], pickHeadsetFirst, now) == "🎧 —   ⌨ —", "G HUB not answering: picked devices keep their places");
var threeAuto = WidgetSettings.Default with { Count = 3 };
Check(DeviceList.PreviewText(four, threeAuto, now) == "🖱 92%   🎧 92%   ⌨ 92%", "count 3, automatic");
var twinsPicked = WidgetSettings.Default with { Count = 3, Devices = [new ChosenDevice("g502x_plus", "MOUSE", "G502 X PLUS")] };
var twinsOneEmpty = twinsUneven.Select(r => r.Id == "dev5" ? DeviceList.WithProblem(r, "x") : r).ToList();
Check(DeviceList.PreviewText(twinsOneEmpty, twinsPicked, now) == "🖱 92%   🖱 —", "two same models: both shown separately, charges not merged");
Check(DeviceList.FlyoutRows(twinsOneEmpty, now, twinsPicked.Devices).Select(x => x.Line).SequenceEqual(["G502 X PLUS (1): 92%", "G502 X PLUS (2): —"]), "two same models in the flyout: two rows");
Console.WriteLine("All checks passed.");
