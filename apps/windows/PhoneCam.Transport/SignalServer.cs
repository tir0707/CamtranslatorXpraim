using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CamtranslatorXpraim.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CamtranslatorXpraim.Transport;

public sealed class SignalServer : IAsyncDisposable
{
    public const int DefaultPort = 47990;
    public PairingGate Pairing { get; } = new();
    public string ReceiverToken { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public Func<string, Task<bool>> Approve { get; set; } = _ => Task.FromResult(false);
    public event Action<string>? Status;
    private readonly SemaphoreSlim pairingLock = new(1, 1);
    private readonly object sync = new();
    private Peer? publisher, receiver;
    private WebApplication? app;
    public bool Connected { get { lock (sync) return publisher is not null; } }
    public string? PublisherAddress { get; private set; }
    public async Task StartAsync(LocalCertificate certificate, string webRoot, int port = DefaultPort, CancellationToken ct = default)
    {
        Pairing.Rotate();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        if (Environment.GetEnvironmentVariable("CAMXP_DIAGNOSTICS") == "1") builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Debug);
        builder.WebHost.ConfigureKestrel(k => { k.ListenAnyIP(port, o => o.UseHttps(certificate.Server)); k.ListenAnyIP(port + 1); });
        app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            if (!context.Request.IsHttps)
            {
                if (context.Request.Path == "/ca.cer")
                {
                    context.Response.ContentType = "application/x-x509-ca-cert";
                    await context.Response.Body.WriteAsync(certificate.Authority.RawData, context.RequestAborted);
                }
                else { context.Response.StatusCode = 404; }
                return;
            }
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; media-src 'self' blob:; img-src 'self' data:; object-src 'none'; frame-ancestors 'none'";
            await next(context);
        });
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
        app.MapGet("/health", () => Results.Json(new { app = "CamtranslatorXpraim", protocol = 1 }));
        app.Map("/signal", HandleAsync);
        app.MapGet("/{**path}", async context =>
        {
            var path = context.Request.RouteValues["path"]?.ToString() ?? "index.html";
            if (path.Length == 0) path = "index.html";
            // Explicit allowlist: no arbitrary files, traversal or private key exposure.
            if (path is not ("index.html" or "receiver.html" or "sender.js" or "receiver.js" or "common.js" or "style.css" or "manifest.webmanifest" or "icon.svg")) { context.Response.StatusCode = 404; return; }
            if (path.StartsWith("receiver", StringComparison.Ordinal) && !IPAddress.IsLoopback(context.Connection.RemoteIpAddress!)) { context.Response.StatusCode = 403; return; }
            context.Response.ContentType = Path.GetExtension(path) switch { ".js" => "text/javascript", ".css" => "text/css", ".svg" => "image/svg+xml", ".webmanifest" => "application/manifest+json", _ => "text/html; charset=utf-8" };
            await context.Response.SendFileAsync(Path.Combine(webRoot, path), context.RequestAborted);
        });
        await app.StartAsync(ct);
    }
    private async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
        var origin = context.Request.Headers.Origin.ToString();
        if (origin.Length > 0 && (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Authority != context.Request.Host.Value)) { context.Response.StatusCode = 403; return; }
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var peer = new Peer(socket);
        var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var isPublisher = false;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            using var hello = await ReadAsync(socket, timeout.Token);
            if (hello.RootElement.GetProperty("type").GetString() != "hello") return;
            var role = hello.RootElement.GetProperty("role").GetString();
            if (role == "receiver")
            {
                var token = hello.RootElement.GetProperty("token").GetString() ?? "";
                if (!IPAddress.IsLoopback(context.Connection.RemoteIpAddress!) || !SecureEquals(token, ReceiverToken)) return;
                lock (sync) { if (receiver is not null) return; receiver = peer; }
                await peer.SendAsync(new { type = "ready" });
                Peer? pub; lock (sync) pub = publisher;
                if (pub is not null) await pub.SendAsync(new { type = "peer-ready" });
            }
            else if (role == "publisher")
            {
                if (!await pairingLock.WaitAsync(0, timeout.Token)) return;
                try
                {
                    lock (sync) if (publisher is not null) return;
                    var code = hello.RootElement.GetProperty("code").GetString() ?? "";
                    if (!Pairing.TryConsume(code, address)) { await peer.SendAsync(new { type = "error", message = "Код неверен, просрочен или превышен лимит попыток." }); return; }
                    Status?.Invoke("Ожидание подтверждения на компьютере");
                    if (!await Approve(address).WaitAsync(timeout.Token)) return;
                    lock (sync) { publisher = peer; PublisherAddress = address; }
                    isPublisher = true;
                    await peer.SendAsync(new { type = "paired", computer = Environment.MachineName });
                    Peer? rec; lock (sync) rec = receiver;
                    if (rec is not null) await peer.SendAsync(new { type = "peer-ready" });
                    Status?.Invoke("Устройство подключено: " + address);
                }
                finally { pairingLock.Release(); }
            }
            else return;
            while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
            {
                using var message = await ReadAsync(socket, context.RequestAborted);
                var type = message.RootElement.GetProperty("type").GetString();
                if (type == "ping") { await peer.SendAsync(new { type = "pong" }); continue; }
                var allowed = isPublisher ? type is "offer" or "ice" or "telemetry" : type is "answer" or "ice" or "command";
                if (!allowed) continue;
                Peer? target; lock (sync) target = isPublisher ? receiver : publisher;
                if (target is not null) await target.SendRawAsync(message.RootElement.GetRawText());
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or IOException) { }
        finally
        {
            Peer? other = null;
            lock (sync)
            {
                if (publisher == peer) { publisher = null; PublisherAddress = null; other = receiver; }
                if (receiver == peer) { receiver = null; other = publisher; }
            }
            if (other is not null) try { await other.SendAsync(new { type = "peer-left" }); } catch (WebSocketException) { }
            if (isPublisher) Status?.Invoke("Устройство отключено");
            socket.Abort();
        }
    }
    public async Task DisconnectAsync()
    {
        Peer? peer; lock (sync) peer = publisher;
        if (peer is not null)
        {
            await peer.SendAsync(new { type = "disconnect" });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await peer.Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Отключено пользователем", timeout.Token);
        }
    }
    public static bool SecureEquals(string a, string b) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    public static async Task<JsonDocument> ReadAsync(WebSocket socket, CancellationToken ct)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[4096];
        WebSocketReceiveResult part;
        do
        {
            part = await socket.ReceiveAsync(buffer, ct);
            if (part.MessageType != WebSocketMessageType.Text) throw new WebSocketException("Требуется JSON");
            stream.Write(buffer, 0, part.Count);
            if (stream.Length > 65536) throw new WebSocketException("Сообщение слишком велико");
        } while (!part.EndOfMessage);
        return JsonDocument.Parse(stream.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
    }
    public async ValueTask DisposeAsync()
    {
        lock (sync) { publisher?.Socket.Abort(); receiver?.Socket.Abort(); }
        if (app is not null) { await app.StopAsync(); await app.DisposeAsync(); }
        pairingLock.Dispose();
    }
    private sealed class Peer(WebSocket socket)
    {
        public WebSocket Socket { get; } = socket;
        private readonly SemaphoreSlim sendLock = new(1, 1);
        public Task SendAsync(object data) => SendRawAsync(JsonSerializer.Serialize(data));
        public async Task SendRawAsync(string data)
        {
            await sendLock.WaitAsync();
            try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await Socket.SendAsync(Encoding.UTF8.GetBytes(data), WebSocketMessageType.Text, true, timeout.Token); }
            finally { sendLock.Release(); }
        }
    }
}
