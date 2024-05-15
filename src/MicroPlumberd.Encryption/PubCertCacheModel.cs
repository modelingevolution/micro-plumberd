using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Encryption;


class PubCertEventHandler(IConfiguration configuration, ILogger<PubCertEventHandler> log) : IEventHandler, ITypeRegister
{
    Task IEventHandler.Handle(Metadata m, object ev) => Given(m, ev);
    public async Task Given(Metadata m, object ev)
    {
        switch (ev)
        {
            case PublicCertificate e: await Given(m, e); break;
            default:
                throw new ArgumentException("Unknown event type", ev.GetType().Name);
        }
    }
    static IEnumerable<Type> ITypeRegister.Types => [typeof(PublicCertificate)];

    public async Task Given(Metadata m, PublicCertificate cert)
    {
        var certDir = configuration.GetValue<string>("CertsPath") ?? "./certs";
        var recipient = m.SourceStreamId.Substring(m.SourceStreamId.IndexOf('-')+1);
        var file = Path.Combine(certDir, $"{recipient}.cer");
        if (!Directory.Exists(certDir))
            Directory.CreateDirectory(certDir);
        try
        {
            if(!File.Exists(file))
                log.LogInformation("Persisting new public certificate: " + recipient);
            await File.WriteAllBytesAsync(file, cert.Data);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex,"Could not save public certificate.");
        }
    }
}