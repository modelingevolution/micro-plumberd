using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MicroPlumberd.Encryption;

class Encryptor(IPlumber plumber, ICertManager certManager) : IEncryptor
{
    private readonly ConcurrentDictionary<Type, IObjectSerializer> _serializers = new();
    private IObjectSerializer Serializer<T>() => _serializers.GetOrAdd(typeof(T), x => plumber.Config.SerializerFactory(x));
    public T Decrypt<T>(OperationContext context, byte[] data, string recipient)
    {
        var certificate = certManager.GetPrivate(recipient);
        using var rsa = certificate.GetRSAPrivateKey();
        var decryptedData = rsa.Decrypt(data, RSAEncryptionPadding.OaepSHA256);
            
        return (T)Serializer<T>().Deserialize(context,decryptedData, typeof(T))!;
    }
    public byte[] Encrypt<T>(OperationContext context, T data,  string recipient)
    {
        var dataToEncrypt = Serializer<T>().Serialize(context, data);
        var certificate = certManager.Get(recipient);
        using var rsa = certificate.GetRSAPublicKey();
        var encryptedData = rsa.Encrypt(dataToEncrypt, RSAEncryptionPadding.OaepSHA256);
        return encryptedData;
    }
}