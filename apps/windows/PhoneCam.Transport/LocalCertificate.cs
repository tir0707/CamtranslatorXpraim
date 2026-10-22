using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CamtranslatorXpraim.Core;

namespace CamtranslatorXpraim.Transport;

public sealed class LocalCertificate : IDisposable
{
    public X509Certificate2 Server { get; }
    public X509Certificate2 Authority { get; }
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Authority.RawData));
    public string PublicPath { get; }
    public LocalCertificate(string folder)
    {
        Directory.CreateDirectory(folder);
        PublicPath = Path.Combine(folder, "CamtranslatorXpraim-CA.cer");
        var pfx = Path.Combine(folder, "authority.pfx");
        if (File.Exists(pfx)) Authority = X509CertificateLoader.LoadPkcs12FromFile(pfx, null, X509KeyStorageFlags.Exportable);
        else
        {
            using var key = RSA.Create(3072);
            var request = new CertificateRequest("CN=CamtranslatorXpraim Local CA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            Authority = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(3));
            File.WriteAllBytes(pfx, Authority.Export(X509ContentType.Pfx));
        }
        File.WriteAllBytes(PublicPath, Authority.Export(X509ContentType.Cert));
        using var serverKey = RSA.Create(2048);
        var leaf = new CertificateRequest("CN=CamtranslatorXpraim", serverKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost"); san.AddDnsName(Environment.MachineName); san.AddDnsName(Environment.MachineName + ".local");
        san.AddIpAddress(IPAddress.Loopback);
        foreach (var address in LocalNetwork.Addresses()) san.AddIpAddress(IPAddress.Parse(address.Address));
        leaf.CertificateExtensions.Add(san.Build());
        leaf.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        leaf.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        var usages = new OidCollection { new("1.3.6.1.5.5.7.3.1") };
        leaf.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, true));
        using var signed = leaf.Create(Authority, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(30), RandomNumberGenerator.GetBytes(16));
        using var withKey = signed.CopyWithPrivateKey(serverKey);
        // Schannel on Windows needs a persisted key container for server credentials.
        Server = X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet);
    }
    public void Dispose() { Server.Dispose(); Authority.Dispose(); }
}
