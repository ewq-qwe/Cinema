using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using Reviews.Domain.ValueObjects;

namespace Reviews.Infrastructure.Persistence.Serializers;

/// <summary>
/// Value Object <see cref="Rating"/> ↔ число <c>rating: 5</c>, як у документі з Лаб. 01 (а не вкладений { value: 5 }).
/// Під час читання VO створюється через конструктор, тож некоректна оцінка з БД теж не пройде.
/// </summary>
public sealed class RatingSerializer : SerializerBase<Rating>
{
    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, Rating value) =>
        context.Writer.WriteInt32(value.Value);

    public override Rating Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var reader = context.Reader;
        return reader.CurrentBsonType switch
        {
            BsonType.Int32 => new Rating(reader.ReadInt32()),
            BsonType.Int64 => new Rating(checked((int)reader.ReadInt64())),
            BsonType.Double => new Rating((int)reader.ReadDouble()),   // mongosh може записати число як double
            var type => throw new FormatException($"Cannot deserialize Rating from BsonType {type}.")
        };
    }
}
