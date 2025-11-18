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
    /// <summary>
    /// Provides Protocol Buffers (protobuf) based serialization for event data and metadata.
    /// Uses protobuf-net for efficient binary serialization with fallback to JSON for dynamic objects.
    /// </summary>
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

        /// <summary>
        /// Gets the JSON serializer options with support for ExpandoObject serialization.
        /// </summary>
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

        /// <summary>
        /// Deserializes the specified byte span into an object of the given type using protobuf-net.
        /// </summary>
        /// <param name="context">The operation context containing request-scoped information.</param>
        /// <param name="span">The byte span containing the serialized event data.</param>
        /// <param name="t">The target type to deserialize into.</param>
        /// <returns>The deserialized object, or <c>null</c> if the span is empty.</returns>
        public object? Deserialize(OperationContext context, ReadOnlySpan<byte> span, Type t)
        {
            return Serializer.NonGeneric.Deserialize(t, span);
        }

        /// <summary>
        /// Parses the metadata byte span into a <see cref="JsonElement"/> using JSON deserialization.
        /// </summary>
        /// <param name="context">The operation context containing request-scoped information.</param>
        /// <param name="span">The byte span containing the serialized metadata.</param>
        /// <returns>A <see cref="JsonElement"/> representing the parsed metadata, or an empty JSON object if the span is empty.</returns>
        public JsonElement ParseMetadata(OperationContext context, ReadOnlySpan<byte> span)
        {
            if (span.Length == 0) return Empty;
            return JsonSerializer.Deserialize<JsonElement>(span, Options);
        }

        /// <summary>
        /// Serializes the specified object into a byte array using protobuf-net.
        /// Uses JSON serialization for <see cref="ExpandoObject"/> instances.
        /// </summary>
        /// <param name="context">The operation context containing request-scoped information.</param>
        /// <param name="t">The object to serialize.</param>
        /// <returns>A byte array containing the serialized object data.</returns>
        public byte[] Serialize(OperationContext context, object? t)
        {

            return _serializes.GetOrAdd(t.GetType(),
                x =>
                {
                    if (x == typeof(ExpandoObject)) return new Json();
                    return (ISerializer)Activator.CreateInstance(typeof(ProtoSerializer<>).MakeGenericType(x));
                }).Serialize(t);
        }

        /// <summary>
        /// Gets the content type for protobuf serialization.
        /// </summary>
        /// <value>
        /// Returns "application/octet-stream" for binary protobuf data.
        /// </value>
        public string ContentType => "application/octet-stream";
    }
}
