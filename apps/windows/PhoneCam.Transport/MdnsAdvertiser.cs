using System.Net;
using System.Net.Sockets;
using System.Text;
using CamtranslatorXpraim.Core;

namespace CamtranslatorXpraim.Transport;

// DNS-SD advertisement for iOS NetServiceBrowser. Manual IP remains available when multicast is filtered.
public sealed class MdnsAdvertiser : IDisposable
{
    private readonly List<UdpClient> clients = [];
    private readonly CancellationTokenSource stop = new();
    private readonly string host = Environment.MachineName.Replace('.', '-') + ".local";
    public void Start(int port)
    {
        foreach (var address in LocalNetwork.Addresses())
        {
            try
            {
                var local = IPAddress.Parse(address.Address);
                var udp = new UdpClient(AddressFamily.InterNetwork);
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, 5353));
                udp.JoinMulticastGroup(IPAddress.Parse("224.0.0.251"), local);
                udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, local.GetAddressBytes());
                udp.MulticastLoopback = true; clients.Add(udp);
                _ = RunAsync(udp, local, port);
            }
            catch (SocketException) { /* Per-interface failure is non-fatal; manual IP is still usable. */ }
        }
    }
    private async Task RunAsync(UdpClient udp, IPAddress address, int port)
    {
        try
        {
            await AnnounceAsync(udp, address, port);
            while (!stop.IsCancellationRequested)
            {
                var packet = await udp.ReceiveAsync(stop.Token);
                if (packet.Buffer.Length >= 12 && (packet.Buffer[2] & 0x80) == 0 && MatchesQuery(packet.Buffer)) await AnnounceAsync(udp, address, port);
            }
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException or ObjectDisposedException) { }
    }
    private bool MatchesQuery(byte[] bytes)
    {
        // DNS label encodings are ASCII for our service/host; no decompression needed for matching initial questions.
        var text = Encoding.ASCII.GetString(bytes);
        return text.Contains("_camxp", StringComparison.OrdinalIgnoreCase) || text.Contains(host.Split('.')[0], StringComparison.OrdinalIgnoreCase) || text.Contains("_services", StringComparison.OrdinalIgnoreCase);
    }
    private Task AnnounceAsync(UdpClient udp, IPAddress address, int port)
    {
        var bytes = BuildPacket(host, address, port);
        return udp.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353));
    }
    public static byte[] BuildPacket(string host, IPAddress address, int port)
    {
        using var stream = new MemoryStream();
        void U16(int value) { stream.WriteByte((byte)(value >> 8)); stream.WriteByte((byte)value); }
        void U32(int value) { U16(value >> 16); U16(value); }
        static byte[] Name(string name)
        {
            using var s = new MemoryStream();
            foreach (var label in name.Split('.', StringSplitOptions.RemoveEmptyEntries)) { var b = Encoding.UTF8.GetBytes(label);if (b.Length > 63) throw new ArgumentException("DNS label");s.WriteByte((byte)b.Length);s.Write(b); }
            s.WriteByte(0);return s.ToArray();
        }
        void Record(string name, int type, byte[] data, bool unique = true) { stream.Write(Name(name));U16(type);U16(unique ? 0x8001 : 1);U32(120);U16(data.Length);stream.Write(data); }
        U16(0);U16(0x8400);U16(0);U16(4);U16(0);U16(0);
        const string service = "_camxp._tcp.local";
        var instance = "CamtranslatorXpraim-" + host.Split('.')[0] + "." + service;
        Record(service, 12, Name(instance), false);
        using var srv = new MemoryStream();srv.Write(new byte[] { 0, 0, 0, 0, (byte)(port >> 8), (byte)port });srv.Write(Name(host));
        Record(instance, 33, srv.ToArray());
        var txt = Encoding.ASCII.GetBytes("protocol=1");Record(instance, 16, [(byte)txt.Length, ..txt]);
        Record(host, 1, address.GetAddressBytes());return stream.ToArray();
    }
    public void Dispose() { stop.Cancel();foreach (var udp in clients) udp.Dispose();stop.Dispose(); }
}
