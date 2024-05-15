namespace MicroPlumberd.Encryption;

public interface IEncryptor
{
    byte[] Encrypt<T>(T data,  string recipient);
    T Decrypt<T>(byte[] data, string recipient);
}