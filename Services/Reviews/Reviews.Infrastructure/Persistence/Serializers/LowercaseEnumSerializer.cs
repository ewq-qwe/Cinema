using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Reviews.Infrastructure.Persistence.Serializers;

/// <summary>Enum ↔ рядок у нижньому регістрі (<c>kind: "full"</c>), як у JSON Schema з Лаб. 01.</summary>
public sealed class LowercaseEnumSerializer<TEnum> : StructSerializerBase<TEnum> where TEnum : struct, Enum
{
    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, TEnum value) =>
        context.Writer.WriteString(value.ToString().ToLowerInvariant());

    public override TEnum Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var reader = context.Reader;
        return reader.CurrentBsonType switch
        {
            BsonType.String when Enum.TryParse<TEnum>(reader.ReadString(), ignoreCase: true, out var parsed) => parsed,
            BsonType.Int32 => (TEnum)Enum.ToObject(typeof(TEnum), reader.ReadInt32()),
            var type => throw new FormatException($"Cannot deserialize {typeof(TEnum).Name} from BsonType {type}.")
        };
    }
}
