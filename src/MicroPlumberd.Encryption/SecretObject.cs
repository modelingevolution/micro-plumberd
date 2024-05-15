namespace MicroPlumberd.Encryption;

public record SecretObject<T>
{
    private T? _value;
    private bool _decrypted;
    private readonly IEncryptor? _encryptor;
        
    private readonly byte[]? _data;
    private readonly string _recipient;
    
    internal byte[] Data => _data;
    internal string Recipient => _recipient;
    public static implicit operator SecretObject<T>(T obj) => new SecretObject<T>(obj);
    public static implicit operator T(SecretObject<T> obj) => obj.Value;
    private SecretObject(T value, string? recipient = null)
    {
        _value = value;
        _recipient = recipient ?? Environment.MachineName;
        _encryptor = null;
        _decrypted = true;
        //_salt = RandomNumberGenerator.GetBytes(16);
    }

    public T? Value
    {
        get
        {
            if (_decrypted) return _value;
            if (_encryptor == null)
                throw new InvalidOperationException("Missing encryptor");
            _value = _encryptor.Decrypt<T>(_data,_recipient);
            _decrypted = true;
            return _value;
        }
    }

    private SecretObject( byte[] enc, string recipient, IEncryptor encryptor)
    {
          
        _data = enc;
        _recipient = recipient;
        _encryptor = encryptor;
    }

    internal static SecretObject<T> Load(byte[] enc, string recipient, IEncryptor encryptor)
    {
        return new SecretObject<T>( enc, recipient, encryptor);
    }
    public static SecretObject<T> Create(T value, string recipient)
    {
        return new SecretObject<T>(value, recipient);
    }
        

        
}