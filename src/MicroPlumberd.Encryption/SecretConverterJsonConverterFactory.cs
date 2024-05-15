using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace MicroPlumberd.Encryption
{
    class SecretConverterJsonConverterFactory(IServiceProvider serviceProvider) : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
        {
            return typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(SecretObject<>);
        }

        public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            var t = typeToConvert.GetGenericArguments()[0];
            var type = typeof(SecretObjectJsonConverter<>).MakeGenericType(t);
            return (JsonConverter)Activator.CreateInstance(type, serviceProvider.GetRequiredService<IEncryptor>())!;
        }
    }
}
