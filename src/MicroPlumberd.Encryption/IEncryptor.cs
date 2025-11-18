namespace MicroPlumberd.Encryption;

/// <summary>
/// Provides encryption and decryption services using X.509 certificates.
/// </summary>
public interface IEncryptor
{
    /// <summary>
    /// Encrypts data for a specified recipient using their public certificate.
    /// </summary>
    /// <typeparam name="T">The type of data to encrypt.</typeparam>
    /// <param name="context">The operation context for the encryption operation.</param>
    /// <param name="data">The data to encrypt.</param>
    /// <param name="recipient">The name of the recipient whose public certificate will be used for encryption.</param>
    /// <returns>The encrypted data as a byte array.</returns>
    byte[] Encrypt<T>(OperationContext context, T data,  string recipient);

    /// <summary>
    /// Decrypts data for a specified recipient using their private certificate.
    /// </summary>
    /// <typeparam name="T">The type of data to decrypt.</typeparam>
    /// <param name="context">The operation context for the decryption operation.</param>
    /// <param name="data">The encrypted data to decrypt.</param>
    /// <param name="recipient">The name of the recipient whose private certificate will be used for decryption.</param>
    /// <returns>The decrypted data.</returns>
    T Decrypt<T>(OperationContext context, byte[] data, string recipient);
}