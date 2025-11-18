namespace MicroPlumberd.Encryption;

/// <summary>
/// Represents an encrypted secret value that is automatically encrypted when serialized and decrypted when accessed.
/// </summary>
/// <typeparam name="T">The type of the secret value.</typeparam>
public record SecretObject<T>
{
    private T? _value;
    private bool _decrypted;
    private readonly IEncryptor? _encryptor;

    private readonly byte[]? _data;
    private readonly string _recipient;

    internal byte[] Data => _data;
    internal string Recipient => _recipient;

    /// <summary>
    /// Implicitly converts a value to a SecretObject.
    /// </summary>
    /// <param name="obj">The value to wrap as a secret.</param>
    public static implicit operator SecretObject<T>(T obj) => new SecretObject<T>(obj);

    /// <summary>
    /// Implicitly converts a SecretObject to its decrypted value.
    /// </summary>
    /// <param name="obj">The SecretObject to unwrap.</param>
    public static implicit operator T(SecretObject<T> obj) => obj.Value;
    private SecretObject(T value, string? recipient = null)
    {
        _value = value;
        _recipient = recipient ?? Environment.MachineName;
        _encryptor = null;
        _decrypted = true;
        //_salt = RandomNumberGenerator.GetBytes(16);
    }

    /// <summary>
    /// Gets the decrypted value of the secret. If the value is encrypted, it will be decrypted on first access.
    /// </summary>
    /// <value>The decrypted secret value.</value>
    /// <exception cref="InvalidOperationException">Thrown when the encryptor is not available for decryption.</exception>
    public T? Value
    {
        get
        {
            if (_decrypted) return _value;
            if (_encryptor == null)
                throw new InvalidOperationException("Missing encryptor");

            using var scope = OperationContext.GetOrCreate(Flow.Request);
            _value = _encryptor.Decrypt<T>(scope.Context,_data,_recipient);
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

    /// <summary>
    /// Creates a new SecretObject with the specified value and recipient.
    /// </summary>
    /// <param name="value">The value to encrypt.</param>
    /// <param name="recipient">The name of the recipient whose public certificate will be used for encryption.</param>
    /// <returns>A new SecretObject containing the encrypted value.</returns>
    public static SecretObject<T> Create(T value, string recipient)
    {
        return new SecretObject<T>(value, recipient);
    }
        

        
}