using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;

namespace MicroPlumberd.Encryption;

class CertManager(IPlumber plumber, IConfiguration configuration) : ICertManager
{
    private ConcurrentDictionary<string, X509Certificate2> _private = new();
    private ConcurrentDictionary<string, X509Certificate2> _public = new();
    public X509Certificate2 Get(string recipient)
    {
        return _public.GetOrAdd(recipient, r =>
        {
            var certDir = configuration.GetValue<string>("CertsPath") ?? "./certs";
            var file = Path.Combine(certDir, r + ".pfx");
            if (!Directory.Exists(certDir))
                Directory.CreateDirectory(certDir);
            if (File.Exists(file))
                return new X509Certificate2(file);
            else
            {
                var cert = GenerateCertificate(r);
                byte[] certData = cert.Export(X509ContentType.Pfx, "");
                File.WriteAllBytes(file, certData);
                return cert;
            }
        });
    }
    public X509Certificate2 GetPrivate(string recipient)
    {
        return _private.GetOrAdd(recipient, r =>
        {
            var certDir = configuration.GetValue<string>("CertsPath") ?? "./certs";
            var file = Path.Combine(certDir, r + ".pfx");
            if (!Directory.Exists(certDir))
                Directory.CreateDirectory(certDir);
            if (File.Exists(file))
                return new X509Certificate2(file, "");
            else
            {
                var cert = GenerateCertificate(r);
                byte[] certData = cert.Export(X509ContentType.Pfx, "");
                File.WriteAllBytes(file, certData);
                return cert;
            }
        });
    }
    static X509Certificate2 GenerateCertificate(string subjectName)
    {
        using var rsa = RSA.Create(2048);

        var req = new CertificateRequest($"CN={subjectName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var start = DateTimeOffset.UtcNow;
        var end = start.AddYears(20);
        var cert = req.CreateSelfSigned(start, end);

        return new X509Certificate2(cert.Export(X509ContentType.Pfx, ""), "", X509KeyStorageFlags.Exportable);
    }

}