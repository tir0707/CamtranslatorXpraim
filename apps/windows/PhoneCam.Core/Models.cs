using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CamtranslatorXpraim.Core;

public sealed record QualityProfile(string Id, string Name, int Width, int Height, int Fps, int Bitrate)
{
    public static readonly QualityProfile[] All = [
        new("economy", "Экономичный · 480p / 30", 640, 480, 30, 900_000),
        new("standard", "Стандартный · 720p / 30", 1280, 720, 30, 2_500_000),
        new("high", "Высокое качество · 1080p / 30", 1920, 1080, 30, 5_000_000),
        new("smooth", "Плавность · 720p / 60", 1280, 720, 60, 4_000_000),
        new("maximum", "Максимальный · 1080p / 60", 1920, 1080, 60, 8_000_000)];
}

public sealed class PairingGate(TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly object sync = new();
    private readonly Dictionary<string, (int Count, DateTimeOffset Until)> attempts = [];
    private string code = "";
    private DateTimeOffset expires;
    public string Code { get { lock (sync) return code; } }
    public string Rotate()
    {
        lock (sync) { code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6"); expires = time.GetUtcNow().AddMinutes(5); return code; }
    }
    public bool TryConsume(string input, string address)
    {
        lock (sync)
        {
            var now = time.GetUtcNow();
            foreach (var key in attempts.Where(x => x.Value.Until < now).Select(x => x.Key).ToArray()) attempts.Remove(key);
            attempts.TryGetValue(address, out var entry);
            if (entry.Count >= 5 && entry.Until > now) return false;
            if (attempts.Count >= 1024 && !attempts.ContainsKey(address)) return false;
            attempts[address] = (entry.Count + 1, now.AddMinutes(5));
            if (now > expires || input.Length != 6 || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(input), Encoding.UTF8.GetBytes(code))) return false;
            Rotate();
            return true;
        }
    }
}

public sealed record NetworkAddress(string Name, string Address, bool UsbCandidate);
public static class LocalNetwork
{
    public static NetworkAddress[] Addresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
            .Select(a => new NetworkAddress(n.Name, a.Address.ToString(),
                n.Description.Contains("Apple", StringComparison.OrdinalIgnoreCase) || n.Description.Contains("iPhone", StringComparison.OrdinalIgnoreCase))))
        .ToArray();
}

public sealed class UserSettings
{
    public string SaveFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "CamtranslatorXpraim");
    public bool MinimizeToTray { get; set; } = true;
    public bool Mirror { get; set; }
    public bool AutoReconnect { get; set; } = true;
    public static UserSettings Load(string path)
    {
        try { return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException) { return new(); }
    }
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }
}
