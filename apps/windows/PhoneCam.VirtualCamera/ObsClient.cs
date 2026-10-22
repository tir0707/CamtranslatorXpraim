using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CamtranslatorXpraim.VirtualCamera;

public sealed class ObsClient : IAsyncDisposable
{
    private readonly ClientWebSocket socket = new();
    private readonly CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
    private static string Hash(string value) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public async Task ConnectAsync(string password, int port = 4455)
    {
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), timeout.Token);
        using var hello = await ReadAsync();
        var data = hello.RootElement.GetProperty("d");
        string? authentication = null;
        if (data.TryGetProperty("authentication", out var auth)) authentication = Hash(Hash(password + auth.GetProperty("salt").GetString()) + auth.GetProperty("challenge").GetString());
        await SendAsync(new { op = 1, d = new { rpcVersion = 1, authentication, eventSubscriptions = 0 } });
        using var identified = await ReadAsync();
        if (identified.RootElement.GetProperty("op").GetInt32() != 2) throw new IOException("OBS не подтвердил авторизацию");
    }
    public async Task<bool> ConfigureAndStartAsync()
    {
        const string scene = "CamtranslatorXpraim", input = "CamXP — iPhone";
        var scenes = await RequestAsync("GetSceneList");
        if (!scenes.GetProperty("scenes").EnumerateArray().Any(x => x.GetProperty("sceneName").GetString() == scene)) await RequestAsync("CreateScene", new { sceneName = scene });
        var inputs = await RequestAsync("GetInputList");
        var exists = inputs.GetProperty("inputs").EnumerateArray().Any(x => x.GetProperty("inputName").GetString() == input);
        if (!exists) await RequestAsync("CreateInput", new { sceneName = scene, inputName = input, inputKind = "window_capture", inputSettings = new { }, sceneItemEnabled = true });
        var windows = await RequestAsync("GetInputPropertiesListPropertyItems", new { inputName = input, propertyName = "window" });
        var window = windows.GetProperty("propertyItems").EnumerateArray().FirstOrDefault(x => (x.GetProperty("itemName").GetString() ?? "").Contains("CamtranslatorXpraim — Видео", StringComparison.Ordinal));
        if (window.ValueKind == JsonValueKind.Undefined) throw new IOException("OBS не видит окно «CamtranslatorXpraim — Видео». Откройте предпросмотр и повторите.");
        await RequestAsync("SetInputSettings", new { inputName = input, inputSettings = new { window = window.GetProperty("itemValue").GetString(), client_area = true, cursor = false, priority = 0 }, overlay = true });
        var items = await RequestAsync("GetSceneItemList", new { sceneName = scene });
        var item = items.GetProperty("sceneItems").EnumerateArray().FirstOrDefault(x => x.GetProperty("sourceName").GetString() == input);
        if (item.ValueKind == JsonValueKind.Undefined)
        {
            var created = await RequestAsync("CreateSceneItem", new { sceneName = scene, sourceName = input, sceneItemEnabled = true });
            item = created;
        }
        var id = item.GetProperty("sceneItemId").GetInt32();
        var video = await RequestAsync("GetVideoSettings");
        await RequestAsync("SetSceneItemTransform", new { sceneName = scene, sceneItemId = id, sceneItemTransform = new { positionX = 0, positionY = 0, boundsType = "OBS_BOUNDS_SCALE_INNER", boundsWidth = video.GetProperty("baseWidth").GetInt32(), boundsHeight = video.GetProperty("baseHeight").GetInt32(), alignment = 5 } });
        await RequestAsync("SetCurrentProgramScene", new { sceneName = scene });
        var before = await RequestAsync("GetVirtualCamStatus");
        if (!before.GetProperty("outputActive").GetBoolean()) await RequestAsync("StartVirtualCam");
        var after = await RequestAsync("GetVirtualCamStatus");
        return after.GetProperty("outputActive").GetBoolean();
    }
    private async Task<JsonElement> RequestAsync(string type, object? data = null)
    {
        var id = Guid.NewGuid().ToString("N");
        await SendAsync(new { op = 6, d = new { requestType = type, requestId = id, requestData = data ?? new { } } });
        while (true)
        {
            using var reply = await ReadAsync();
            if (reply.RootElement.GetProperty("op").GetInt32() != 7) continue;
            var d = reply.RootElement.GetProperty("d");
            if (d.GetProperty("requestId").GetString() != id) continue;
            var status = d.GetProperty("requestStatus");
            if (!status.GetProperty("result").GetBoolean()) throw new IOException($"OBS {type}: {status.GetProperty("code")}: {(status.TryGetProperty("comment", out var comment) ? comment.GetString() : "Ошибка запроса")}");
            return d.TryGetProperty("responseData", out var result) ? result.Clone() : JsonSerializer.SerializeToElement(new { });
        }
    }
    private Task SendAsync(object value) => socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)), WebSocketMessageType.Text, true, timeout.Token);
    private async Task<JsonDocument> ReadAsync()
    {
        using var data = new MemoryStream();var bytes = new byte[8192]; WebSocketReceiveResult part;
        do { part = await socket.ReceiveAsync(bytes, timeout.Token); if (part.MessageType != WebSocketMessageType.Text) throw new IOException("OBS закрыл соединение"); data.Write(bytes, 0, part.Count); if (data.Length > 1024 * 1024) throw new IOException("Слишком большой ответ OBS"); } while (!part.EndOfMessage);
        return JsonDocument.Parse(data.ToArray());
    }
    public ValueTask DisposeAsync() { socket.Dispose(); timeout.Dispose(); return ValueTask.CompletedTask; }
}
