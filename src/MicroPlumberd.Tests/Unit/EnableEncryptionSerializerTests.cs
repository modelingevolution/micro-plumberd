using System.Text;
using FluentAssertions;
using KurrentDB.Client;
using MicroPlumberd.Encryption;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd.Tests.Unit;

/// <summary>
/// <c>EnableEncryption</c> must not touch the process-wide default serializer options.
/// <para>
/// It used to add its converter to <see cref="JsonObjectSerializer.Options"/> when a plumber was created. In a
/// process where anything had already serialized with those options (any earlier test, any earlier host) that
/// threw "JsonSerializerOptions instance is read-only" — SecretTests.HandleCommand, green alone, red in the
/// suite. And where it did not throw, the converter it added was bound to the FIRST container's services for
/// the life of the process: every later plumber encrypted and decrypted with someone else's keys.
/// </para>
/// No KurrentDB: creating a <see cref="PlumberEngine"/> does not connect.
/// </summary>
public class EnableEncryptionSerializerTests
{
    private static readonly KurrentDBClientSettings Unused = KurrentDBClientSettings.Create("esdb://127.0.0.1:1?tls=false");

    public record Holder(SecretObject<string> Password);

    [Fact]
    public void EnablingEncryptionAfterTheDefaultOptionsWereUsed_Works()
    {
        // The in-suite order: something in this process has already serialized with the defaults.
        new JsonObjectSerializer().Serialize(OperationContext.Create(Flow.Request), new { used = true });

        var engine = PlumberEngine.Create(Unused, c => { c.ServiceProvider = Services(new TagEncryptor("A")); c.EnableEncryption(); });

        Written(engine).Should().Be("A:pw");
    }

    [Fact]
    public void EachPlumberEncryptsWithItsOwnContainersEncryptor()
    {
        var a = PlumberEngine.Create(Unused, c => { c.ServiceProvider = Services(new TagEncryptor("A")); c.EnableEncryption(); });
        var b = PlumberEngine.Create(Unused, c => { c.ServiceProvider = Services(new TagEncryptor("B")); c.EnableEncryption(); });

        Written(a).Should().Be("A:pw");
        Written(b).Should().Be("B:pw", "the second container's keys, not the first one's");
    }

    /// <summary>
    /// AddPlumberd runs the configure callback (where EnableEncryption is called) BEFORE it sets the
    /// config's ServiceProvider: the encryptor must come from the provider set afterwards.
    /// </summary>
    [Fact]
    public void TheEncryptorComesFromTheProviderSetAfterEnableEncryption_AsAddPlumberdDoes()
    {
        var engine = PlumberEngine.Create(Unused, c => { c.EnableEncryption(); c.ServiceProvider = Services(new TagEncryptor("A")); });

        Written(engine).Should().Be("A:pw");
    }

    [Fact]
    public void ThePlumberReadsBackWhatItWrote()
    {
        var engine = PlumberEngine.Create(Unused, c => { c.ServiceProvider = Services(new TagEncryptor("A")); c.EnableEncryption(); });
        var serializer = engine.SerializerFactory(typeof(Holder));
        var bytes = serializer.Serialize(OperationContext.Create(Flow.Request), new Holder("pw"));

        var read = (Holder)serializer.Deserialize(OperationContext.Create(Flow.Request), bytes, typeof(Holder))!;

        read.Password.Value.Should().Be("pw");
    }

    [Fact]
    public void TheProcessWideDefaultsAreNotChanged()
    {
        PlumberEngine.Create(Unused, c => { c.ServiceProvider = Services(new TagEncryptor("A")); c.EnableEncryption(); });

        JsonObjectSerializer.Options.Converters.Select(x => x.GetType().Name)
            .Should().NotContain("SecretConverterJsonConverterFactory", "a plumber without encryption must not get it");
    }

    [Fact]
    public void ACustomSerializerFactoryIsKept()
    {
        var custom = new JsonObjectSerializer(new System.Text.Json.JsonSerializerOptions());
        var engine = PlumberEngine.Create(Unused, c =>
        {
            c.ServiceProvider = Services(new TagEncryptor("A"));
            c.SerializerFactory = _ => custom;
            c.EnableEncryption();
        });

        engine.SerializerFactory(typeof(Holder)).Should().BeSameAs(custom,
            "encryption replaces only the default JSON serializer, never one the application chose");
    }

    private static string Written(PlumberEngine engine)
    {
        var json = Encoding.UTF8.GetString(engine.SerializerFactory(typeof(Holder))
            .Serialize(OperationContext.Create(Flow.Request), new Holder("pw")));
        var data = System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("Password").GetProperty("Data").GetString()!;
        return Encoding.UTF8.GetString(Convert.FromBase64String(data));
    }

    private static IServiceProvider Services(IEncryptor encryptor) =>
        new ServiceCollection().AddSingleton(encryptor).BuildServiceProvider();

    private sealed class TagEncryptor(string tag) : IEncryptor
    {
        public byte[] Encrypt<T>(OperationContext context, T data, string recipient) =>
            Encoding.UTF8.GetBytes($"{tag}:{data}");

        public T Decrypt<T>(OperationContext context, byte[] data, string recipient) =>
            (T)(object)Encoding.UTF8.GetString(data)[(tag.Length + 1)..];
    }
}
