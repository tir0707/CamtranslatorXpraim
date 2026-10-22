using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using CamtranslatorXpraim.Transport;

namespace CamtranslatorXpraim.Tests;
public static class Smoke
{
    [STAThread]
    public static void Main()
    {
        var app = new Application();var web = new WebView2();var window = new Window { Title = "CamXP — synthetic WebRTC test (not iPhone)", Width = 680, Height = 480, Content = web };
        var output = Path.GetFullPath("artifacts/webrtc-smoke.json");Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var folder = Path.GetFullPath("artifacts/smoke-data");
        SignalServer? server = null;LocalCertificate? cert = null;
        var completed = false;
        async Task Finish(object result, int exitCode)
        {
            if (completed) return;completed = true;
            File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            if (server is not null) await server.DisposeAsync();web.Dispose();cert?.Dispose();app.Shutdown(exitCode);
        }
        window.Loaded += async (_, _) =>
        {
            try
            {
                cert = new LocalCertificate(folder);server = new SignalServer { Approve = _ => Task.FromResult(true) };
                await server.StartAsync(cert, Path.GetFullPath("apps/web-fallback"), 48994);
                var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(folder, "webview"), new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
                await web.EnsureCoreWebView2Async(environment);
                web.CoreWebView2.ServerCertificateErrorDetected += (_, e) => { using var c = X509Certificate2.CreateFromPem(e.ServerCertificate.ToPemEncoding());if (c.Thumbprint == cert.Server.Thumbprint) e.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow; };
                web.CoreWebView2.WebMessageReceived += async (_, e) =>
                {
                    using var d = JsonDocument.Parse(e.WebMessageAsJson);
                    if (d.RootElement.GetProperty("type").GetString() != "smoke-result") return;
                    var ok = d.RootElement.GetProperty("ok").GetBoolean();
                    if (ok) { await using var image = File.Create(Path.GetFullPath("artifacts/synthetic-webrtc-frame.png"));await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image); }
                    await Finish(d.RootElement.Clone(), ok ? 0 : 1);
                };
                web.CoreWebView2.NavigationCompleted += async (_, e) =>
                {
                    if (!e.IsSuccess) { await Finish(new { ok = false, error = e.WebErrorStatus.ToString() }, 1);return; }
                    var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "webrtc-smoke.js"));
                    await web.CoreWebView2.ExecuteScriptAsync("globalThis.SMOKE_CODE=" + JsonSerializer.Serialize(server.Pairing.Code) + ";" + script);
                };
                web.Source = new Uri($"https://127.0.0.1:48994/receiver.html#token={server.ReceiverToken}");
                _ = Task.Delay(45000).ContinueWith(_ => app.Dispatcher.InvokeAsync(async () => await Finish(new { ok = false, error = "45s timeout" }, 1)));
            }
            catch (Exception e) { await Finish(new { ok = false, error = e.ToString() }, 1); }
        };
        app.Run(window);
    }
}
