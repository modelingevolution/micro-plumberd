using System.Buffers;
using System.Collections.Concurrent;
using System.Text.Json;
using ProtoBuf;
using ProtoBuf.Serializers;

namespace MicroPlumberd.Protobuf
{
    public class ProtoBuffObjectSerialization : IObjectSerializer
    {
        interface ISerializer
        {
            byte[] Serialize(object? t);
        }

        class ProtoSerializer<T> : ISerializer
        {
            public byte[] Serialize(object? t)
            {
                using var s = new MemoryStream();
                Serializer.Serialize(s, (T)t);
                return s.ToArray();
            }
        }
        private static readonly ConcurrentDictionary<Type, ISerializer> _serializes =
            new ConcurrentDictionary<Type, ISerializer>();
        public static JsonSerializerOptions Options = new() { Converters = { new ExpandoObjectConverter() } };
        private static JsonElement Empty = JsonSerializer.Deserialize<JsonElement>("{}");
        public object? Deserialize(ReadOnlySpan<byte> span, Type t)
        {
            return Serializer.NonGeneric.Deserialize(t, span);
        }

        public JsonElement ParseMetadata(ReadOnlySpan<byte> span)
        {
            if (span.Length == 0) return Empty;
            return JsonSerializer.Deserialize<JsonElement>(span, Options);
        }

        public byte[] Serialize(object? t)
        {
            return _serializes.GetOrAdd(t.GetType(),
                x => (ISerializer)Activator.CreateInstance(typeof(ProtoSerializer<>).MakeGenericType(x))).Serialize(t);
        }

        public string ContentType => "application/octet-stream";
    }
}
