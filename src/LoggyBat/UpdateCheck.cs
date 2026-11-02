using System.Globalization;
using System.Text.Json;

namespace LoggyBat;

public enum UpdateStatus { NoUpdate, Available, Failed }

// Latest is the version of the newest published release (null when the check failed).
public sealed record UpdateResult(UpdateStatus Status, Version? Latest, string? Url, string? Error, DateTimeOffset At)
{
    public string Text => Status switch
    {
        UpdateStatus.Available => $"Доступна версия {Latest}",
        UpdateStatus.NoUpdate => $"Обновлений нет (последний релиз {Latest})",
        _ => $"Не удалось проверить: {Error}",
    };
}

// Reading GitHub's "latest release" reply. Pure, so tests/GHubProtocolCheck covers it.
public static class UpdateCheck
{
    public const string LatestApi = "https://api.github.com/repos/mrmaxxxx/LoggyBat/releases/latest";
    public const string ReleasesPage = "https://github.com/mrmaxxxx/LoggyBat/releases/";

    // "v1.1.20.0" -> 1.1.20.0. Two to four plain numbers, compared as numbers (1.1.9 < 1.1.10);
    // anything else is no version at all, so it can never produce the red dot.
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrEmpty(tag))
            return null;

        var parts = (tag[0] is 'v' or 'V' ? tag[1..] : tag).Split('.');
        if (parts.Length is < 2 or > 4)
            return null;

        var n = new int[4];
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length is 0 or > 9 || !parts[i].All(char.IsAsciiDigit) ||
                !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out n[i]))
            {
                return null;
            }
        }

        return new Version(n[0], n[1], n[2], n[3]);
    }

    public static UpdateResult Evaluate(string json, Version installed, DateTimeOffset at)
    {
        UpdateResult Failed(string why) => new(UpdateStatus.Failed, null, null, why, at);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var release = doc.RootElement;
            string? tag = Str(release, "tag_name");
            string? url = Str(release, "html_url");

            if (Flag(release, "draft") || Flag(release, "prerelease"))
                return Failed("GitHub вернул черновик или предварительный выпуск");
            if (ParseTag(tag) is not { } latest)
                return Failed($"некорректная версия в релизе ({tag ?? "нет"})");
            // The flyout opens this link, so only the project's own release pages are accepted.
            if (url is null || !url.StartsWith(ReleasesPage, StringComparison.Ordinal) ||
                !Uri.TryCreate(url, UriKind.Absolute, out _) || url.Any(char.IsWhiteSpace))
                return Failed("неожиданная ссылка на релиз");

            return latest > installed
                ? new UpdateResult(UpdateStatus.Available, latest, url, null, at)
                : new UpdateResult(UpdateStatus.NoUpdate, latest, url, null, at);
        }
        catch (JsonException)
        {
            return Failed("непонятный ответ GitHub");
        }
    }

    // The red dot: only a found release newer than what is installed now (also after an update).
    public static bool ShowsDot(UpdateResult? result, Version installed) =>
        result is { Status: UpdateStatus.Available, Latest: { } latest } && latest > installed;

    private static string? Str(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static bool Flag(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}

// Whether THIS widget instance may start an automatic check. WidBar gives no "settings saved" event,
// so: while the settings window is open nothing starts (the switch there may still be a draft); once it
// is closed, the last JSON WidBar handed over is the real state (Save keeps the last draft, Cancel
// sends the original back). The settle time covers Cancel's JSON arriving just after the window closes.
public sealed class AutoCheckPolicy(string? savedJson)
{
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(10);

    private bool _enabled = WidgetSettings.Parse(savedJson).AutoUpdateCheck;
    private bool _settingsOpen;
    private DateTimeOffset _closedAt = DateTimeOffset.MinValue;

    public void OnSettingsJson(string json) => _enabled = WidgetSettings.Parse(json).AutoUpdateCheck;
    public void OnSettingsOpened() => _settingsOpen = true;

    public void OnSettingsClosed(DateTimeOffset now)
    {
        _settingsOpen = false;
        _closedAt = now;
    }

    public bool MayStart(DateTimeOffset now) => _enabled && !_settingsOpen && now - _closedAt >= Settle;
}

// The request itself and its last result. One shared instance per process on purpose (unlike the
// rest of the widget): all widget instances live in this process, show the same result and share one
// throttle. Tests build their own with a fake GitHub (HttpMessageHandler) and clock.
public sealed class UpdateChecker(HttpMessageHandler handler, Func<DateTimeOffset> clock)
{
    public static UpdateChecker Shared { get; } = new(new SocketsHttpHandler(), () => DateTimeOffset.Now);

    private readonly HttpClient _http = new(handler) { Timeout = TimeSpan.FromSeconds(10) };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _stateFile;

    public UpdateResult? Last { get; private set; }

    // The newest release found newer than the installed version. A later failed check keeps it;
    // only a successful "no updates" answer clears it.
    public UpdateResult? Found { get; private set; }

    // internal, not private: the JSON serializer builds it by reflection.
    internal sealed record Stored(UpdateResult? Last, UpdateResult? Found);

    // Raised on a background thread after every request.
    public event Action? Changed;

    // The last result survives restarts, so a restart neither repeats the request nor loses the red dot.
    public void Load(string dataDirectory)
    {
        if (_stateFile is not null)
            return;

        _stateFile = Path.Combine(dataDirectory, "update-check.json");
        try
        {
            if (File.Exists(_stateFile) && JsonSerializer.Deserialize<Stored>(File.ReadAllText(_stateFile)) is { } stored)
                (Last, Found) = (stored.Last, stored.Found);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            (Last, Found) = (null, null); // unreadable: as if never checked
        }
    }

    // One step of an instance's background loop: asks GitHub only if this instance's policy allows it
    // and the last answer is old enough (a day; an hour after a failure).
    public Task AutoCheckAsync(AutoCheckPolicy policy, Version installed, CancellationToken ct) =>
        policy.MayStart(clock()) ? RunAsync(installed, onlyIfDue: true, ct) : Task.CompletedTask;

    // The "Check now" button: always asks GitHub, whatever the automatic check is set to.
    public Task<UpdateResult> CheckNowAsync(Version installed) => RunAsync(installed, onlyIfDue: false, CancellationToken.None);

    private async Task<UpdateResult> RunAsync(Version installed, bool onlyIfDue, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            // Checked under the lock: two instances starting together make one request, not two.
            if (onlyIfDue && Last is { } last)
            {
                var age = clock() - last.At;
                var interval = last.Status == UpdateStatus.Failed ? TimeSpan.FromHours(1) : TimeSpan.FromDays(1);
                if (age >= TimeSpan.Zero && age < interval)
                    return last;
            }

            var result = await RequestAsync(installed, ct);
            Last = result;
            if (result.Status != UpdateStatus.Failed)
                Found = result.Status == UpdateStatus.Available ? result : null;
            Save();
            Log.Write($"Проверка обновлений: {result.Text}");
            Changed?.Invoke();
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Sends only what any HTTPS request carries plus "LoggyBat/<version>" as User-Agent (GitHub requires one).
    private async Task<UpdateResult> RequestAsync(Version installed, CancellationToken ct)
    {
        UpdateResult Failed(string why) => new(UpdateStatus.Failed, null, null, why, clock());
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UpdateCheck.LatestApi);
            request.Headers.UserAgent.ParseAdd($"LoggyBat/{installed}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return Failed($"GitHub ответил {(int)response.StatusCode}");

            return UpdateCheck.Evaluate(await response.Content.ReadAsStringAsync(ct), installed, clock());
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            return Failed("нет связи с GitHub");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failed("GitHub не ответил за 10 с");
        }
    }

    private void Save()
    {
        if (_stateFile is null)
            return;
        try
        {
            File.WriteAllText(_stateFile, JsonSerializer.Serialize(new Stored(Last, Found)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Only the throttle is lost; the next start checks again.
        }
    }
}
