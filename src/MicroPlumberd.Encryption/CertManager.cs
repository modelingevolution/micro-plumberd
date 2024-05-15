using System.Collections.Concurrent;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace MicroPlumberd.Encryption;

[OutputStream("PublicCertificate")]
class PublicCertificate
{
    public byte[] Data { get; set; }
}

public class CertificateNotFoundException : Exception
{
    public string Recipient { get; init; }
}

class CertManagerInitializer(ICertManager cm, IPlumber plumber) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await cm.Init();
        await plumber.Subscribe("$et-PublicCertificateSnapShotted", FromRelativeStreamPosition.Start)
            .WithSnapshotHandler<PubCertEventHandler>();
    }
}

class CertManager(IPlumber plumber, IConfiguration configuration) : ICertManager
{
    private ConcurrentDictionary<string, X509Certificate2> _private = new();
    private ConcurrentDictionary<string, X509Certificate2> _public = new();
    public X509Certificate2 Get(string recipient)
    {
        return _public.GetOrAdd(recipient, r =>
        {
            var certDir = configuration.GetValue<string>("CertsPath") ?? "./certs";
            
            if (!Directory.Exists(certDir))
                Directory.CreateDirectory(certDir);
            var file = Path.Combine(certDir, $"{r}.cer");
            if (File.Exists(file))
                return new X509Certificate2(file);
            file = Path.Combine(certDir, $"{r}.pfx");
            if (File.Exists(file))
                return new X509Certificate2(file);
            throw new CertificateNotFoundException() { Recipient = recipient};
        });
    }

    public async Task Init()
    {
        var r = Environment.MachineName;
        var certDir = configuration.GetValue<string>("CertsPath") ?? "./certs";
        var file = Path.Combine(certDir, r + ".pfx");
        if (!Directory.Exists(certDir))
            Directory.CreateDirectory(certDir);
        if (File.Exists(file))
        {
            var result = await plumber.GetState<PublicCertificate>(r);
            if (result == null)
            {
                // stores are not in sync.
                var cert = new X509Certificate2(file);
                byte[] pubCer = cert.Export(X509ContentType.Cert);
                PublicCertificate pc = new PublicCertificate() { Data = pubCer };
                await plumber.AppendState(pc, r);
            }
        }
        else
        {
            var cert = GenerateCertificate(r);
            byte[] certData = cert.Export(X509ContentType.Pfx, "");
            byte[] pubCer = cert.Export(X509ContentType.Cert);
            await File.WriteAllBytesAsync(file, certData);
            PublicCertificate pc = new PublicCertificate() { Data = pubCer };
            await plumber.AppendState(pc, r);
        }

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
                byte[] pubCer = cert.Export(X509ContentType.Cert);
                File.WriteAllBytes(file, certData);
                PublicCertificate pc = new PublicCertificate() { Data = pubCer };
                Task.Run(() => plumber.AppendState(pc, recipient));
                
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