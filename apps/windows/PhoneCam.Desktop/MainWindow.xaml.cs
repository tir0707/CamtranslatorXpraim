using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using CamtranslatorXpraim.Core;
using CamtranslatorXpraim.Transport;
using CamtranslatorXpraim.Video;
using CamtranslatorXpraim.VirtualCamera;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace CamtranslatorXpraim.Desktop;
public partial class MainWindow : Window
{
    private readonly string dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CamtranslatorXpraim");
    private readonly SignalServer server = new();
    private readonly MdnsAdvertiser mdns = new();
    private LocalCertificate? certificate;
    private UserSettings settings = new();
    private WebView2? web;
    private Window? preview;
    private System.Windows.Forms.NotifyIcon? tray;
    private bool exiting, ready;
    private string SettingsPath => Path.Combine(dataFolder, "settings.json");
    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await StartAsync();
        Closing += OnClosing;
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized && TrayBox.IsChecked == true) Hide(); };
        PreviewKeyDown += (_, e) => { if (Keyboard.Modifiers != ModifierKeys.Control) return; if (e.Key == Key.S) Command("snapshot"); if (e.Key == Key.R) Command("record"); };
    }
    private async Task StartAsync()
    {
        try
        {
            settings = UserSettings.Load(SettingsPath); TrayBox.IsChecked = settings.MinimizeToTray; MirrorBox.IsChecked = settings.Mirror;
            using (var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) StartupBox.IsChecked = run?.GetValue("CamtranslatorXpraim") is not null;
            certificate = new LocalCertificate(dataFolder);
            server.Approve = address => Dispatcher.InvokeAsync(() => System.Windows.MessageBox.Show(this, $"Разрешить передачу камеры с устройства {address}?", "Сопряжение CamtranslatorXpraim", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes).Task;
            server.Status += text => Dispatcher.BeginInvoke(() => { StatusText.Text = text; CodeText.Text = server.Pairing.Code; });
            await server.StartAsync(certificate, Path.Combine(AppContext.BaseDirectory, "web"));
            mdns.Start(SignalServer.DefaultPort);
            CodeText.Text = server.Pairing.Code;
            CertificateText.Text = $"Сертификат: {certificate.PublicPath}\nSHA-256: {certificate.Fingerprint}";
            Network_Click(this, new RoutedEventArgs());
            tray = new System.Windows.Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "CamtranslatorXpraim", Visible = true };
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Открыть", null, (_, _) => Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; Activate(); }));
            menu.Items.Add("Выход", null, (_, _) => Dispatcher.Invoke(Close)); tray.ContextMenuStrip = menu;
            tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; Activate(); });
            await CreatePreviewAsync(); ready = true;
            StatusText.Text = "Ожидание iPhone · сервер готов";
        }
        catch (Exception e) { StatusText.Text = "Не удалось запустить: " + e.Message; }
    }
    private async Task CreatePreviewAsync()
    {
        if (certificate is null) return;
        web = new WebView2();
        preview = new Window { Title = "CamtranslatorXpraim — Видео", Width = 960, Height = 580, MinWidth = 320, MinHeight = 240, Background = System.Windows.Media.Brushes.Black, Content = web };
        preview.Closing += (_, e) => { if (!exiting) { e.Cancel = true; preview.Hide(); } };
        preview.Show();
        var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(dataFolder, "WebView2"), new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
        await web.EnsureCoreWebView2Async(environment);
        web.CoreWebView2.Settings.AreDevToolsEnabled = false;
        web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        web.CoreWebView2.ServerCertificateErrorDetected += (_, e) =>
        {
            // Trust only this process's exact leaf, and only its loopback endpoint.
            using var actual = X509Certificate2.CreateFromPem(e.ServerCertificate.ToPemEncoding());
            if (Uri.TryCreate(e.RequestUri, UriKind.Absolute, out var uri) && uri.Host == "127.0.0.1" && uri.Port == SignalServer.DefaultPort && actual.Thumbprint == certificate.Server.Thumbprint) e.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow;
        };
        web.CoreWebView2.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith($"https://127.0.0.1:{SignalServer.DefaultPort}/receiver.html", StringComparison.Ordinal)) e.Cancel = true; };
        web.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
        web.CoreWebView2.WebMessageReceived += (_, e) => OnWebMessage(e.WebMessageAsJson);
        web.CoreWebView2.DownloadStarting += (_, e) =>
        {
            Directory.CreateDirectory(settings.SaveFolder);
            e.ResultFilePath = Path.Combine(settings.SaveFolder, Path.GetFileName(e.ResultFilePath)); e.Handled = true;
        };
        web.Source = new Uri($"https://127.0.0.1:{SignalServer.DefaultPort}/receiver.html#token={server.ReceiverToken}");
    }
    private void OnWebMessage(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);var m = d.RootElement;
            switch (m.GetProperty("type").GetString())
            {
                case "status": StatusText.Text = m.GetProperty("text").GetString(); break;
                case "error": StatusText.Text = "Ошибка: " + m.GetProperty("text").GetString(); break;
                case "recording": RecordButton.Content = m.GetProperty("active").GetBoolean() ? "■ Остановить запись" : "Запись · Ctrl+R"; break;
                case "stats":
                    double Number(string key) => m.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
                    double? Optional(string key) => m.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
                    MetricsText.Text = new VideoStats((int)Number("width"), (int)Number("height"), Number("fps"), Number("bitrate"), Optional("rtt"), Optional("jitterMs"), Optional("decodeMs"), Number("loss")).Display + $"\nДекодер: {(m.TryGetProperty("decoder", out var decoder) ? decoder.GetString() : "—")}";
                    break;
            }
        }
        catch (JsonException) { StatusText.Text = "Некорректная диагностика приёмника"; }
    }
    private void Command(string action, object? value = null) => web?.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { action, value }));
    private void Preview_Click(object sender, RoutedEventArgs e) { preview?.Show(); preview?.Activate(); }
    private async void Disconnect_Click(object sender, RoutedEventArgs e) { try { await server.DisconnectAsync(); } catch (Exception ex) { StatusText.Text = ex.Message; } }
    private void Code_Click(object sender, RoutedEventArgs e) => CodeText.Text = server.Pairing.Rotate();
    private void Network_Click(object sender, RoutedEventArgs e)
    {
        AddressesText.Text = string.Join("\n", LocalNetwork.Addresses().Select(a => $"{a.Name}{(a.UsbCandidate ? " · возможный USB" : "")}\nhttps://{a.Address}:{SignalServer.DefaultPort}\nСертификат: http://{a.Address}:{SignalServer.DefaultPort + 1}/ca.cer"));
    }
    private void Snapshot_Click(object sender, RoutedEventArgs e) => Command("snapshot");
    private void Record_Click(object sender, RoutedEventArgs e) => Command("record");
    private void Mirror_Changed(object sender, RoutedEventArgs e) { if (ready) Command("mirror", MirrorBox.IsChecked == true); }
    private async void Obs_Click(object sender, RoutedEventArgs e)
    {
        try { preview?.Show(); preview?.Activate(); await using var obs = new ObsClient(); await obs.ConnectAsync(ObsPassword.Password); StatusText.Text = await obs.ConfigureAndStartAsync() ? "OBS сообщает: виртуальная камера активна. Выберите OBS Virtual Camera в нужной программе." : "OBS не подтвердил запуск виртуальной камеры"; }
        catch (Exception ex) { StatusText.Text = "OBS: " + ex.Message; }
    }
    private void Certificate_Click(object sender, RoutedEventArgs e) => Open(dataFolder);
    private void Folder_Click(object sender, RoutedEventArgs e) { Directory.CreateDirectory(settings.SaveFolder); Open(settings.SaveFolder); }
    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = settings.SaveFolder };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) { settings.SaveFolder = dialog.SelectedPath; settings.Save(SettingsPath); }
    }
    private static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (StartupBox.IsChecked == true) key.SetValue("CamtranslatorXpraim", $"\"{Environment.ProcessPath}\""); else key.DeleteValue("CamtranslatorXpraim", false);
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (exiting) return;
        e.Cancel = true; exiting = true;
        settings.MinimizeToTray = TrayBox.IsChecked == true; settings.Mirror = MirrorBox.IsChecked == true; settings.Save(SettingsPath);
        Command("shutdown"); tray?.Dispose();
        mdns.Dispose(); await server.DisposeAsync(); certificate?.Dispose(); preview?.Close(); Close();
    }
}
