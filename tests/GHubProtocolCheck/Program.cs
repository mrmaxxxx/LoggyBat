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
Check(DeviceList.FlyoutText(mine, null, now) == "🖱 G502 X PLUS: 92%\n🎧 PRO X WIRELESS: 69% ⚡", "flyout for mouse + headset: names and charge only (no diagnostics)");
Check(DeviceList.FlyoutRows(mine, now).SequenceEqual([("MOUSE", "G502 X PLUS", "92%", false, 92), ("HEADSET", "PRO X WIRELESS", "69%", true, 69)]), "flyout rows: name, percent, bolt, bar level");
Check(DeviceList.PreviewItems(mine, now).SequenceEqual([("MOUSE", "92%", false), ("HEADSET", "69%", true)]), "taskbar items: percent and charging separately (orange bolt)");

var four = Poll([], [Other("dev0"), Keyboard("dev1"), Headset("dev2"), Mouse("dev3")], true, now, Charge92);
Check(DeviceList.PreviewText(four, now) == "🖱 92%   🎧 92%", "3+ devices: taskbar shows mouse, headset");
Check(DeviceList.ForDisplay(four).Select(x => x.Row.Type).SequenceEqual(["MOUSE", "HEADSET", "KEYBOARD", "SPEAKER"]), "flyout order: mouse, headset, keyboard, others");
Check(DeviceList.FlyoutText(four, null, now).Contains("⌨ G915: 92%") && DeviceList.FlyoutText(four, null, now).Contains("🔋 Z407: 92%"), "keyboard ⌨, unknown type 🔋");

// --- Icons ---
Check(DeviceList.Icon("MICROPHONE") == "🎤" && DeviceList.Icon("SOMETHING_NEW") == "🔋" && DeviceList.Icon("") == "🔋", "microphone 🎤, unknown type 🔋");
Check(new[] { "MOUSE", "HEADSET", "KEYBOARD", "MICROPHONE", "SPEAKER", "" }.Select(DeviceList.IconKind).SequenceEqual(["mouse", "headset", "keyboard", "microphone", "adapter", "adapter"]), "vector icons: unknown types get the adapter");
Check(DeviceList.PreviewItems([], now).SequenceEqual([("", "—", false)]), "no devices: one neutral item");
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
Check(rows[1].Problem == "устройство выключено?" && rows[1].GoneSince is null && DeviceList.FlyoutText(rows, null, now).Contains("🎧 PRO X WIRELESS: —"), "present but no charge != gone; flyout shows '—' (reason only in the log)");
Check(DeviceList.FlyoutRows(rows, now)[1] is { Percent: "—", Level: 0, Charging: false }, "no charge: '—' and an empty bar");

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
Check(DeviceList.FlyoutText(off1, null, now.AddSeconds(2)) == "🖱 G502 X PLUS: —\n🎧 PRO X WIRELESS: 92%" && off1.Single(r => r.Id == "dev00000001").Problem == "не подключено (выключено?)", "mouse off: '—' in the flyout, the reason kept for the log");
Check(DeviceList.InactiveProblem("SOMETHING_ELSE") == "не активно в G HUB", "other inactive states keep the old text");
var on2 = Poll(off1, listed, false, now.AddSeconds(12), Charge92);
Check(DeviceList.PreviewText(on2, now.AddSeconds(12)) == "🖱 92%   🎧 92%", "mouse back on: charge shown again");

// --- Settings: defaults, damaged input, unknown and newer fields ---
static WidgetSettings S(string json) => WidgetSettings.Parse(json);
Check(S("") == WidgetSettings.Default && S("not json") == WidgetSettings.Default && S("[1,2]") == WidgetSettings.Default, "no/damaged settings -> defaults");
Check(WidgetSettings.Default is { Count: 2, Devices.Count: 0, AutoUpdateCheck: true, FromNewerVersion: false }, "clean install: 2 devices, automatic, update check on");
Check(S("""{"count":7,"autoUpdateCheck":false}""") is { Count: 2, AutoUpdateCheck: false }, "bad field -> default for that field only");
const string Old119 = """{"version":1,"count":2,"devices":[],"diagnostics":true,"autoUpdateCheck":true}""";
Check(S(Old119) is { Count: 2, AutoUpdateCheck: true } && S(Old119).ToJson(Old119).Contains("\"diagnostics\":true"), "settings of 1.1.19 (with 'diagnostics') load; the old field is kept, not used");
Check(S("""{"count":3,"devices":[{"model":"g502x_plus","type":"MOUSE","name":"G502 X PLUS"},{"model":"g502x_plus"},{"type":"X"},5]}""") is { Count: 3, Devices: [{ Model: "g502x_plus", Type: "MOUSE" }] }, "devices: duplicates and entries without a model skipped");
var withExtra = """{"count":1,"futureOption":{"a":1},"version":1}""";
var saved = S(withExtra) with { Count = 3 };
Check(saved.ToJson(withExtra).Contains("\"futureOption\":{\"a\":1}") && S(saved.ToJson(withExtra)).Count == 3, "unknown fields survive a save");
Check(S("""{"version":2,"count":3,"newThing":true}""") is { FromNewerVersion: true, Count: 3 }, "settings of a newer version are recognised (shown, not overwritten)");
Check(S(WidgetSettings.Default.ToJson(null)) is { Count: 2, Devices.Count: 0, AutoUpdateCheck: true, FromNewerVersion: false } && !WidgetSettings.Default.ToJson(null).Contains("diagnostics"), "default settings round-trip, no 'diagnostics' written");

// --- Picked devices on the taskbar ---
var pickMouse = WidgetSettings.Default with { Count = 1, Devices = [new ChosenDevice("g502x_plus", "MOUSE", "G502 X PLUS")] };
Check(DeviceList.PreviewText(four, pickMouse, now) == "🖱 92%", "count 1, mouse picked");
var pickHeadsetFirst = WidgetSettings.Default with { Devices = [new ChosenDevice("pro_71_gaming_wireless_headset", "HEADSET", "PRO X WIRELESS"), new ChosenDevice("g915", "KEYBOARD", "G915")] };
Check(DeviceList.PreviewText(four, pickHeadsetFirst, now) == "🎧 92%   ⌨ 92%", "picked order is kept, mouse not picked -> not shown");
var headsetOnly = Poll([], [Mouse("dev1"), Keyboard("dev2")], true, now, Charge92);
Check(DeviceList.PreviewText(headsetOnly, pickHeadsetFirst, now) == "🎧 —   ⌨ 92%", "picked device missing -> '—' in its place, nothing moves in");
Check(DeviceList.FlyoutText(headsetOnly, null, now).Contains("G502 X PLUS") && DeviceList.FlyoutRows(headsetOnly, now, pickHeadsetFirst.Devices)[0] is { Name: "PRO X WIRELESS", Percent: "—", Level: 0 }, "flyout: missing picked device first as '—', others still listed");
Check(DeviceList.PreviewText([], pickHeadsetFirst, now) == "🎧 —   ⌨ —", "G HUB not answering: picked devices keep their places");
var threeAuto = WidgetSettings.Default with { Count = 3 };
Check(DeviceList.PreviewText(four, threeAuto, now) == "🖱 92%   🎧 92%   ⌨ 92%", "count 3, automatic");
var twinsPicked = WidgetSettings.Default with { Count = 3, Devices = [new ChosenDevice("g502x_plus", "MOUSE", "G502 X PLUS")] };
var twinsOneEmpty = twinsUneven.Select(r => r.Id == "dev5" ? DeviceList.WithProblem(r, "x") : r).ToList();
Check(DeviceList.PreviewText(twinsOneEmpty, twinsPicked, now) == "🖱 92%   🖱 —", "two same models: both shown separately, charges not merged");
Check(DeviceList.FlyoutRows(twinsOneEmpty, now, twinsPicked.Devices).Select(x => $"{x.Name}: {x.Percent}").SequenceEqual(["G502 X PLUS (1): 92%", "G502 X PLUS (2): —"]), "two same models in the flyout: two rows");
// --- Bars ---
var levels = DeviceList.Reconcile([], [Mouse("dev1")], true, now).Select(r => DeviceList.ApplyBattery(r, J(Charge92), now)).ToList();
Check(DeviceList.FlyoutRows(levels, now)[0].Level == 92 && DeviceList.FlyoutRows(levels, now.AddSeconds(41))[0] is { Level: 0, Percent: "—" }, "bar follows the shown charge; a stale charge empties it");
// --- Update check: tags compared as numbers, bad replies never give the red dot ---
var installed = new Version(1, 1, 15, 0);
Check(UpdateCheck.ParseTag("v1.1.20.0") == new Version(1, 1, 20, 0) && UpdateCheck.ParseTag("1.2") == new Version(1, 2, 0, 0), "tags v1.1.20.0 and 1.2 parsed");
Check(new[] { null, "", "v", "1", "v1.1.x.0", "1.1.15.0-beta", "1..2", "1.2.3.4.5", "v-1.2", " 1.2", "1.2 ", "99999999999.1" }.All(t => UpdateCheck.ParseTag(t) is null), "malformed tags rejected");
Check(UpdateCheck.ParseTag("v1.1.100.0") > UpdateCheck.ParseTag("v1.1.20.0"), "1.1.100 > 1.1.20: numbers, not strings");
static string Release(string tag, string url = "https://github.com/mrmaxxxx/LoggyBat/releases/tag/x", bool pre = false) =>
    JsonSerializer.Serialize(new { tag_name = tag, html_url = url, draft = false, prerelease = pre });
var newer = UpdateCheck.Evaluate(Release("v1.1.20.0"), installed, now);
Check(newer is { Status: UpdateStatus.Available, Latest: { Build: 20 } } && UpdateCheck.ShowsDot(newer, installed), "newer release -> available, red dot");
Check(UpdateCheck.Evaluate(Release("v1.1.15.0"), installed, now) is { Status: UpdateStatus.NoUpdate, Text: "Обновлений нет (последний релиз 1.1.15.0)" }, "same version -> no update");
Check(UpdateCheck.Evaluate(Release("v1.1.9.0"), installed, now).Status == UpdateStatus.NoUpdate, "older release (1.1.9 < 1.1.15) -> no update");
var badTag = UpdateCheck.Evaluate(Release("latest"), installed, now);
Check(badTag.Status == UpdateStatus.Failed && !UpdateCheck.ShowsDot(badTag, installed) && badTag.Text.StartsWith("Не удалось проверить"), "malformed tag -> failed, no red dot");
Check(UpdateCheck.Evaluate(Release("v9.0.0.0", "https://evil.example/LoggyBat/releases/"), installed, now).Status == UpdateStatus.Failed, "foreign release link rejected");
Check(UpdateCheck.Evaluate(Release("v9.0.0.0", pre: true), installed, now).Status == UpdateStatus.Failed, "pre-release ignored");
Check(UpdateCheck.Evaluate("<html>", installed, now).Status == UpdateStatus.Failed && UpdateCheck.Evaluate("[]", installed, now).Status == UpdateStatus.Failed, "garbage reply -> failed");
Check(!UpdateCheck.ShowsDot(newer, new Version(1, 1, 20, 0)) && !UpdateCheck.ShowsDot(null, installed), "after updating to that version the dot is gone");
// --- Update checker against a fake GitHub: no network, no published test release ---
var clock = now;
var github = new FakeGitHub();
var checker = new UpdateChecker(github, () => clock);
var stateDir = Directory.CreateTempSubdirectory("loggybat-check").FullName;
checker.Load(stateDir);
var autoOn = new AutoCheckPolicy(null);   // clean install: automatic check on
const string SavedOff = """{"autoUpdateCheck":false}""";
const string SavedOn = """{"autoUpdateCheck":true}""";

// 1. A newer version is found.
github.Reply(Release("v1.1.20.0"));
await checker.AutoCheckAsync(autoOn, installed, default);
Check(github.Requests == 1 && checker.Found?.Latest == new Version(1, 1, 20, 0) && UpdateCheck.ShowsDot(checker.Found, installed), "transition: newer version found -> red dot");
Check(github.LastUrl == UpdateCheck.LatestApi && github.LastUserAgent == "LoggyBat/1.1.15.0", "request goes to the GitHub API with User-Agent LoggyBat/<version>");
var restarted = new UpdateChecker(new FakeGitHub(), () => clock);
restarted.Load(stateDir);
await restarted.AutoCheckAsync(autoOn, installed, default);
Check(restarted.Found?.Latest == new Version(1, 1, 20, 0), "after a restart the found version (and the dot) is still known, without a request");
clock = clock.AddHours(5);
await checker.AutoCheckAsync(autoOn, installed, default);
Check(github.Requests == 1, "automatic check: no second request within a day");

// 2. The next check fails: the found version and the dot stay.
clock = clock.AddDays(1);
github.Fail(System.Net.HttpStatusCode.InternalServerError);
await checker.AutoCheckAsync(autoOn, installed, default);
Check(github.Requests == 2 && checker.Last is { Status: UpdateStatus.Failed, Error: "GitHub ответил 500" } && UpdateCheck.ShowsDot(checker.Found, installed), "transition: re-check failed -> error recorded, found version and dot kept");
clock = clock.AddMinutes(30);
await checker.AutoCheckAsync(autoOn, installed, default);
Check(github.Requests == 2, "after a failure: no retry within an hour");
clock = clock.AddMinutes(31);
github.Throw(new HttpRequestException("offline"));
await checker.AutoCheckAsync(autoOn, installed, default);
Check(github.Requests == 3 && checker.Last?.Error == "нет связи с GitHub" && UpdateCheck.ShowsDot(checker.Found, installed), "no network: retried after an hour, dot kept");

// 3. A successful check finds no newer version any more (e.g. the release was withdrawn): the dot goes.
clock = clock.AddHours(2);
github.Reply(Release("v1.1.15.0"));
await checker.AutoCheckAsync(autoOn, installed, default);
Check(checker.Last?.Status == UpdateStatus.NoUpdate && checker.Found is null && !UpdateCheck.ShowsDot(checker.Found, installed), "transition: successful check without a newer version -> dot gone");

// 4. The installed version already equals the found one (the user updated): no dot, no request needed.
var found = UpdateCheck.Evaluate(Release("v1.1.20.0"), installed, now);
Check(UpdateCheck.ShowsDot(found, installed) && !UpdateCheck.ShowsDot(found, new Version(1, 1, 20, 0)), "transition: installed version equals the found one -> no dot");

// Manual check: always asks, also when the automatic check is off and the last answer is fresh.
var manualBefore = github.Requests;
github.Reply(Release("v1.1.15.0"));
var manual = await checker.CheckNowAsync(installed);
Check(github.Requests == manualBefore + 1 && manual.Text == "Обновлений нет (последний релиз 1.1.15.0)", "'Check now' always asks GitHub and returns a visible result");
github.Fail(System.Net.HttpStatusCode.Forbidden);
Check((await checker.CheckNowAsync(installed)).Text == "Не удалось проверить: GitHub ответил 403", "'Check now' error is returned as text (always shown)");

// --- Automatic check: saved setting vs draft, without restarting WidBar ---
var later = clock.AddDays(10);
Check(!new AutoCheckPolicy(SavedOff).MayStart(later), "saved off: no automatic request, also right at start");
Check(new AutoCheckPolicy(SavedOn).MayStart(later) && autoOn.MayStart(later), "saved on (or clean install): may start");
var policy = new AutoCheckPolicy(SavedOn);
policy.OnSettingsOpened();
policy.OnSettingsJson(SavedOff);
Check(!policy.MayStart(later), "settings window open: nothing starts while the switch is a draft");
policy.OnSettingsJson(SavedOn);                 // Cancel hands the original back
policy.OnSettingsClosed(later);
Check(!policy.MayStart(later.AddSeconds(5)) && policy.MayStart(later.AddSeconds(11)), "Cancel: original 'on' restored after the settle time");
policy.OnSettingsOpened();
policy.OnSettingsJson(SavedOff);                // the user switches off and clicks Save
policy.OnSettingsClosed(later);
Check(!policy.MayStart(later.AddMinutes(1)) && !policy.MayStart(later.AddDays(3)), "saved off: no new background request afterwards, without a WidBar restart");
policy.OnSettingsOpened();
policy.OnSettingsJson(SavedOn);                 // draft 'on' while the saved state is 'off'
Check(!policy.MayStart(later.AddDays(3)), "draft 'on' does not start a request before Save");

// A request already sent finishes; no new one starts after 'off' is saved.
var slow = new FakeGitHub();
var release = new TaskCompletionSource();
slow.Hold(release.Task, Release("v1.1.20.0"));
var slowChecker = new UpdateChecker(slow, () => clock);
var slowPolicy = new AutoCheckPolicy(SavedOn);
var inFlight = slowChecker.AutoCheckAsync(slowPolicy, installed, default);
Check(slow.Requests == 1 && !inFlight.IsCompleted, "request sent and waiting for GitHub");
slowPolicy.OnSettingsOpened();
slowPolicy.OnSettingsJson(SavedOff);
slowPolicy.OnSettingsClosed(clock);
release.SetResult();
await inFlight;
Check(slowChecker.Found?.Latest == new Version(1, 1, 20, 0), "the request already sent completes normally");
clock = clock.AddDays(2);
await slowChecker.AutoCheckAsync(slowPolicy, installed, default);
Check(slow.Requests == 1, "after saving 'off' no new request is started");

// --- Several widget instances share one checker, each with its own setting ---
var sharedGitHub = new FakeGitHub();
var sharedChecker = new UpdateChecker(sharedGitHub, () => clock);
var instanceOff = new AutoCheckPolicy(SavedOff);
var instanceOn1 = new AutoCheckPolicy(null);
var instanceOn2 = new AutoCheckPolicy(SavedOn);
await sharedChecker.AutoCheckAsync(instanceOff, installed, default);
Check(sharedGitHub.Requests == 0, "instance with automatic check off does not start one itself");
sharedGitHub.Reply(Release("v1.1.20.0"));
await Task.WhenAll(sharedChecker.AutoCheckAsync(instanceOn1, installed, default), sharedChecker.AutoCheckAsync(instanceOn2, installed, default));
Check(sharedGitHub.Requests == 1, "two instances starting together make one request");
await sharedChecker.AutoCheckAsync(instanceOff, installed, default);
Check(sharedGitHub.Requests == 1 && UpdateCheck.ShowsDot(sharedChecker.Found, installed), "the 'off' instance still starts nothing, but sees the shared result");
sharedGitHub.Reply(Release("v1.1.20.0"));
await sharedChecker.CheckNowAsync(installed);
Check(sharedGitHub.Requests == 2, "manual check stays available in the 'off' instance");
Directory.Delete(stateDir, recursive: true);
// --- Diagnostics text for the log ---
var reasons = new AutoCheckPolicy(SavedOn);
Check(reasons.Reason(later) == AutoCheckPolicy.Allowed && new AutoCheckPolicy(SavedOff).Reason(later) == "запрещена: автопроверка выключена", "log reason: allowed / switched off");
reasons.OnSettingsOpened();
Check(reasons.Reason(later) == "запрещена: окно настроек открыто", "log reason: settings window open");
reasons.OnSettingsClosed(later);
Check(reasons.Reason(later.AddSeconds(1)) == "запрещена: пауза 10 с после закрытия окна настроек" && reasons.Enabled, "log reason: settle pause; last received value kept");
Check(DeviceList.ShortId("0123456789abcdef") == "01234567" && DeviceList.ShortId("ab") == "ab" && DeviceList.ShortId("") == "?", "instance id shortened for the log");
Console.WriteLine("All checks passed.");

// GitHub stand-in: answers queued in order; an unexpected request fails the test.
sealed class FakeGitHub : HttpMessageHandler
{
    private readonly Queue<Func<Task<HttpResponseMessage>>> _replies = new();
    private int _requests;

    public int Requests => _requests;
    public string? LastUrl { get; private set; }
    public string? LastUserAgent { get; private set; }

    public void Reply(string json) => _replies.Enqueue(() => Task.FromResult(Ok(json)));
    public void Fail(System.Net.HttpStatusCode code) => _replies.Enqueue(() => Task.FromResult(new HttpResponseMessage(code)));
    public void Throw(Exception e) => _replies.Enqueue(() => Task.FromException<HttpResponseMessage>(e));
    public void Hold(Task until, string json) => _replies.Enqueue(async () => { await until; return Ok(json); });

    private static HttpResponseMessage Ok(string json) =>
        new(System.Net.HttpStatusCode.OK) { Content = new StringContent(json) };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Interlocked.Increment(ref _requests);
        LastUrl = request.RequestUri?.ToString();
        LastUserAgent = request.Headers.UserAgent.ToString();
        if (_replies.Count == 0)
            throw new InvalidOperationException("FAILED: unexpected request to GitHub");
        return _replies.Dequeue()();
    }
}
