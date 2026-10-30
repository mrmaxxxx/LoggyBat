using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text.Json;

namespace LoggyBat;

// What the widget knows about one device. ReceivedAt is when G HUB last returned
// a charge for it, not when the battery was measured (G HUB does not say that).
public sealed record DeviceReading(
    int? Percent,
    bool Charging,
    DateTimeOffset? ReceivedAt,
    string? Problem)
{
    public static readonly DeviceReading None =
        new(null, false, null, "ждём ответа G HUB");
}

internal sealed class GHubException(string message) : Exception(message);

// One connection to G HUB at a time, owned by a single control loop:
// connect -> every PollInterval re-read the device list and each battery ->
// on any failure drop the socket and reconnect with backoff.
// Polling, not SUBSCRIBE events: the events are not verified yet.
public sealed class GHubClient : IAsyncDisposable
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private static readonly Uri Endpoint = new("ws://localhost:9010");
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);
    // A wall-clock gap this much longer than PollInterval means the PC slept.
    private static readonly TimeSpan SleepGap = TimeSpan.FromSeconds(30);
    private const int MaxMessageBytes = 4 * 1024 * 1024;

    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();

    private Task? _run;
    private ClientWebSocket? _socket;
    private long _nextMsgId;
    private bool _polledThisSession;
    private volatile bool _active = true;
    private volatile DeviceReading _mouse = DeviceReading.None;
    private volatile DeviceReading _headset = DeviceReading.None;

    public DeviceReading Mouse => _mouse;
    public DeviceReading Headset => _headset;

    // Raised on a background thread after every poll and every failure.
    public event Action? Changed;

    public void Start() => _run ??= Task.Run(() => RunAsync(_shutdown.Token));

    // While inactive (widget hidden) nothing is polled; activating polls right away.
    public void SetActive(bool active)
    {
        _active = active;
        if (active)
            Wake();
    }

    private void Wake()
    {
        try
        {
            if (_wake.CurrentCount == 0)
                _wake.Release();
        }
        catch (Exception e) when (e is SemaphoreFullException or ObjectDisposedException)
        {
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        TimeSpan backoff = FirstBackoff;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                while (!_active)
                    await _wake.WaitAsync(ct);

                await SessionAsync(ct);
                continue; // the session asked for an immediate reconnect (after sleep)
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                if (e is not GHubException)
                    Log.Write($"G HUB: {e.GetType().Name}: {e.Message}");
                SetProblemForAll(Describe(e));
            }

            if (_polledThisSession)
                backoff = FirstBackoff;

            try
            {
                await _wake.WaitAsync(backoff, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
        }
    }

    // Returns normally only when the PC has slept; any failure throws.
    private async Task SessionAsync(CancellationToken ct)
    {
        _polledThisSession = false;

        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Origin", "file://");
        socket.Options.AddSubProtocol("json");

        using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            connectTimeout.CancelAfter(RequestTimeout);
            try
            {
                await socket.ConnectAsync(Endpoint, connectTimeout.Token);
            }
            catch (Exception e) when (!ct.IsCancellationRequested &&
                e is WebSocketException or OperationCanceledException)
            {
                throw new GHubException("G HUB не запущен");
            }
        }

        Log.Write("G HUB: подключено");

        using var sessionEnd = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _socket = socket;
        Task reader = ReadLoopAsync(socket, sessionEnd.Token);

        try
        {
            while (true)
            {
                await PollAsync(ct);
                _polledThisSession = true;

                DateTime polledAt = DateTime.UtcNow;
                do
                {
                    await _wake.WaitAsync(_active ? PollInterval : Timeout.InfiniteTimeSpan, ct);
                }
                while (!_active);

                // After sleep the socket can look open while G HUB no longer answers.
                if (DateTime.UtcNow - polledAt > PollInterval + SleepGap)
                {
                    Log.Write("G HUB: долгая пауза (сон или скрытый виджет), переподключаюсь");
                    return;
                }
            }
        }
        finally
        {
            _socket = null;
            sessionEnd.Cancel();
            socket.Abort();
            await reader;
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        // Re-read every time: dev-ids change when G HUB restarts or devices reconnect.
        JsonElement list = await RequestAsync("/devices/list", ct);
        string code = GHubProtocol.ResultCode(list);
        if (code != "SUCCESS")
            throw new GHubException($"G HUB не отдал список устройств ({code})");

        var mouse = await ReadDeviceAsync(list, GHubProtocol.MousePid, _mouse, ct);
        var headset = await ReadDeviceAsync(list, GHubProtocol.HeadsetPid, _headset, ct);
        Publish(mouse, headset);
    }

    private async Task<DeviceReading> ReadDeviceAsync(
        JsonElement list, int pid, DeviceReading previous, CancellationToken ct)
    {
        var device = GHubProtocol.FindDevice(list, pid);
        if (device is null)
            return previous with { Percent = null, Charging = false, Problem = "нет в списке G HUB" };

        if (!device.Active)
            return previous with { Percent = null, Charging = false, Problem = "не активно в G HUB" };

        JsonElement reply = await RequestAsync($"/battery/{device.Id}/state", ct);
        var battery = GHubProtocol.ParseBattery(reply, out string? problem);

        return battery is null
            ? previous with { Percent = null, Charging = false, Problem = problem }
            : new DeviceReading(battery.Percent, battery.Charging, DateTimeOffset.Now, null);
    }

    private async Task<JsonElement> RequestAsync(string path, CancellationToken ct)
    {
        var socket = _socket ?? throw new GHubException("нет соединения с G HUB");
        string id = Interlocked.Increment(ref _nextMsgId).ToString(CultureInfo.InvariantCulture);
        var reply = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = reply;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            byte[] message = JsonSerializer.SerializeToUtf8Bytes(new { msgId = id, verb = "GET", path });

            await _sendLock.WaitAsync(timeout.Token);
            try
            {
                await socket.SendAsync(message.AsMemory(), WebSocketMessageType.Text, true, timeout.Token);
            }
            finally
            {
                _sendLock.Release();
            }

            return await reply.Task.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new GHubException("G HUB не отвечает");
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task ReadLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();

        try
        {
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer.AsMemory(), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                message.Write(buffer, 0, result.Count);
                if (message.Length > MaxMessageBytes)
                    break;

                if (!result.EndOfMessage)
                    continue;

                Dispatch(message.GetBuffer().AsMemory(0, (int)message.Length));
                message.SetLength(0);
            }
        }
        catch
        {
            // Socket closed or aborted: the control loop notices and reconnects.
        }
        finally
        {
            foreach (var pending in _pending.Values)
                pending.TrySetException(new GHubException("соединение с G HUB прервано"));

            if (!ct.IsCancellationRequested)
                Wake(); // G HUB dropped us: don't wait for the next poll to find out
        }
    }

    private void Dispatch(ReadOnlyMemory<byte> json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("msgId", out var id) &&
                id.ValueKind == JsonValueKind.String &&
                _pending.TryRemove(id.GetString()!, out var reply))
            {
                reply.TrySetResult(doc.RootElement.Clone());
            }
        }
        catch (JsonException)
        {
            // Not ours to answer; a request waiting for it will time out.
        }
    }

    private void SetProblemForAll(string problem)
    {
        Publish(
            _mouse with { Percent = null, Charging = false, Problem = problem },
            _headset with { Percent = null, Charging = false, Problem = problem });
    }

    private void Publish(DeviceReading mouse, DeviceReading headset)
    {
        if (mouse.Problem != _mouse.Problem)
            Log.Write($"Мышка: {mouse.Problem ?? "заряд получен"}");
        if (headset.Problem != _headset.Problem)
            Log.Write($"Наушники: {headset.Problem ?? "заряд получен"}");

        _mouse = mouse;
        _headset = headset;
        Changed?.Invoke();
    }

    private static string Describe(Exception e) => e switch
    {
        GHubException => e.Message,
        _ => "ошибка связи с G HUB",
    };

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();

        if (_run is not null)
        {
            try
            {
                await _run;
            }
            catch
            {
                // Shutting down; nothing left to report.
            }
        }

        _shutdown.Dispose();
    }
}
