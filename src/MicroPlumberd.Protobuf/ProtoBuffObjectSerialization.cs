using System.Buffers;
using System.Collections.Concurrent;
using System.Dynamic;
using System.Text.Json;
using MicroPlumberd.Services;
using ProtoBuf;
using ProtoBuf.Meta;
using ProtoBuf.Serializers;

namespace MicroPlumberd.Protobuf
{
    public class ProtoBuffObjectSerialization : IObjectSerializer
    {

        interface ISerializer
        {
            byte[] Serialize(object? t);
        }

        class Json : ISerializer
        {
            public byte[] Serialize(object? t)
            {
                return JsonSerializer.SerializeToUtf8Bytes(t, ProtoBuffObjectSerialization.Options);
            }
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

        static ProtoBuffObjectSerialization()
        {
            //var model = RuntimeTypeModel.Default;
            //var type = model.Add(typeof(CommandExecuted), applyDefaultBehaviour: false);
            //model.Add
            //type.AddField(1, "CommandId");
            //type.AddField(2, "Duration");

            //var type2 = model.Add(typeof(CommandFailed), applyDefaultBehaviour: false);
            //type2.AddField(1, "CommandId");
            //type2.AddField(2, "Duration");
        }
        public object? Deserialize(OperationContext context, ReadOnlySpan<byte> span, Type t)
        {
            return Serializer.NonGeneric.Deserialize(t, span);
        }

        public JsonElement ParseMetadata(OperationContext context, ReadOnlySpan<byte> span)
        {
            if (span.Length == 0) return Empty;
            return JsonSerializer.Deserialize<JsonElement>(span, Options);
        }

        public byte[] Serialize(OperationContext context, object? t)
        {
            
            return _serializes.GetOrAdd(t.GetType(),
                x =>
                {
                    if (x == typeof(ExpandoObject)) return new Json();
                    return (ISerializer)Activator.CreateInstance(typeof(ProtoSerializer<>).MakeGenericType(x));
                }).Serialize(t);
        }

        public string ContentType => "application/octet-stream";
    }
}
