using System.Security.Cryptography.X509Certificates;

namespace MicroPlumberd.Encryption;

/// <summary>
/// Manages X.509 certificates for encryption and decryption operations.
/// </summary>
public interface ICertManager
{
    /// <summary>
    /// Retrieves the private certificate for the specified recipient.
    /// </summary>
    /// <param name="recipient">The name of the recipient whose private certificate to retrieve.</param>
    /// <returns>The X.509 certificate containing the private key.</returns>
    X509Certificate2 GetPrivate(string recipient);

    /// <summary>
    /// Retrieves the public certificate for the specified recipient.
    /// </summary>
    /// <param name="recipient">The name of the recipient whose public certificate to retrieve.</param>
    /// <returns>The X.509 certificate containing the public key.</returns>
    X509Certificate2 Get(string recipient);

    /// <summary>
    /// Initializes the certificate manager, ensuring certificates are loaded and synchronized.
    /// </summary>
    /// <returns>A task representing the asynchronous initialization operation.</returns>
    Task Init();
}