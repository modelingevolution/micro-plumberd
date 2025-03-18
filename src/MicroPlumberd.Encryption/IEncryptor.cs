namespace MicroPlumberd.Encryption;

public interface IEncryptor
{
    byte[] Encrypt<T>(OperationContext context, T data,  string recipient);
    T Decrypt<T>(OperationContext context, byte[] data, string recipient);
}