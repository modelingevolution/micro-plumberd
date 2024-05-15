using System.Security.Cryptography.X509Certificates;

namespace MicroPlumberd.Encryption;

public interface ICertManager
{
    X509Certificate2 GetPrivate(string recipient);
    X509Certificate2 Get(string recipient);
}