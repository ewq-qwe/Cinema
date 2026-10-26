using System.Reflection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using Reviews.Application.Common.Models;
using Reviews.Domain.Common;
using Reviews.Domain.Entities;
using Reviews.Domain.Enums;
using Reviews.Domain.ValueObjects;
using Reviews.Infrastructure.Persistence.Serializers;

namespace Reviews.Infrastructure.Persistence;

/// <summary>
/// Увесь BSON-мапінг — тут, через BsonClassMap: доменні класи не мають жодних BSON-атрибутів.
/// Документи мають структуру з Лаб. 01: camelCase, вкладені author / stats, масиви comments / lastMessages.
/// Реєструється один раз під час старту, до першого звернення до БД.
/// </summary>
public static class MongoMappings
{
    private static readonly Lock Sync = new();
    private static bool _registered;

    public static void Register()
    {
        lock (Sync)
        {
            if (_registered) return;
            _registered = true;

            ConventionRegistry.Register("cinema-reviews", new ConventionPack
            {
                new CamelCaseElementNameConvention(),       // MovieTitle → movieTitle
                new IgnoreExtraElementsConvention(true),   // гнучка схема: зайві поля в документі не ламають читання
                new IgnoreIfNullConvention(true)           // необов'язкові поля відсутні, а не null (title, hasSpoilers…)
            }, type => type.Namespace?.StartsWith("Reviews.", StringComparison.Ordinal) == true);

            BsonSerializer.RegisterSerializer(new DateTimeSerializer(DateTimeKind.Utc)); // дати читаються як UTC

            RegisterDomain();
            RegisterReadModels();
        }
    }

    private static void RegisterDomain()
    {
        BsonClassMap.RegisterClassMap<BaseEntity>(cm =>
        {
            cm.AutoMap();                     // Id (_id), CreatedAt, UpdatedAt, Version — приватні/protected setter-и
            cm.MapIdMember(e => e.Id);
        });

        BsonClassMap.RegisterClassMap<Review>(cm =>
        {
            cm.AutoMap();
            UsePrivateConstructor(cm);
            cm.MapMember(r => r.Kind).SetSerializer(new LowercaseEnumSerializer<ReviewKind>());
            cm.MapMember(r => r.Rating).SetSerializer(new RatingSerializer());

            // Публічні read-only колекції лише відкривають приватні поля — у документ вони не потрапляють
            cm.UnmapMember(r => r.Comments);
            cm.UnmapMember(r => r.Photos);
            cm.UnmapMember(r => r.Pros);
            cm.UnmapMember(r => r.Cons);

            // Приватні поля-колекції. Масив з'являється лише у виду відгуку, якому він належить (гнучка схема)
            cm.MapField("_comments").SetElementName("comments")
              .SetShouldSerializeMethod(o => ((Review)o).Kind == ReviewKind.Full);
            cm.MapField("_photos").SetElementName("photos")
              .SetShouldSerializeMethod(o => ((Review)o).Photos.Count > 0);
            cm.MapField("_pros").SetElementName("pros")
              .SetShouldSerializeMethod(o => ((Review)o).Kind == ReviewKind.Critic);
            cm.MapField("_cons").SetElementName("cons")
              .SetShouldSerializeMethod(o => ((Review)o).Kind == ReviewKind.Critic);
        });

        BsonClassMap.RegisterClassMap<Comment>(cm =>
        {
            cm.AutoMap();
            UsePrivateConstructor(cm);
        });

        BsonClassMap.RegisterClassMap<Discussion>(cm =>
        {
            cm.AutoMap();
            UsePrivateConstructor(cm);
            cm.UnmapMember(d => d.Tags);
            cm.UnmapMember(d => d.LastMessages);
            cm.MapField("_tags").SetElementName("tags");
            cm.MapField("_lastMessages").SetElementName("lastMessages");
        });

        BsonClassMap.RegisterClassMap<Message>(cm =>
        {
            cm.AutoMap();
            UsePrivateConstructor(cm);
        });

        // Value Objects: незмінні (без setter-ів) — під час читання створюються через конструктор
        BsonClassMap.RegisterClassMap<Author>(cm =>
        {
            cm.MapProperty(a => a.CustomerId).SetElementName("customerId");
            cm.MapProperty(a => a.DisplayName).SetElementName("displayName");
            cm.MapCreator(a => new Author(a.CustomerId, a.DisplayName));
        });

        BsonClassMap.RegisterClassMap<DiscussionStats>(cm =>
        {
            cm.MapProperty(s => s.MessagesCount).SetElementName("messagesCount");
            // у нової теми поля немає (null не пишемо — JSON Schema з Лаб. 01 вимагає тип date) → creator отримає null
            cm.MapProperty(s => s.LastActivityAt).SetElementName("lastActivityAt").SetIgnoreIfNull(true).SetDefaultValue((DateTime?)null);
            cm.MapCreator(s => new DiscussionStats(s.MessagesCount, s.LastActivityAt));
        });

        BsonClassMap.RegisterClassMap<MessagePreview>(cm =>
        {
            cm.MapProperty(m => m.MessageId).SetElementName("messageId");
            cm.MapProperty(m => m.Author).SetElementName("author");
            cm.MapProperty(m => m.Excerpt).SetElementName("excerpt");
            cm.MapProperty(m => m.CreatedAt).SetElementName("createdAt");
            cm.MapCreator(m => new MessagePreview(m.MessageId, m.Author, m.Excerpt, m.CreatedAt));
        });
    }

    /// <summary>
    /// Сутності читаються через приватний конструктор без параметрів, а стан відновлюється з полів.
    /// AutoMap бачить приватні setter-и як «незмінний тип» і підхоплює публічні конструктори з валідацією —
    /// прибираємо їх, щоб читання з БД не повторювало бізнес-логіку створення (Touch, перевірки виду тощо).
    /// </summary>
    private static void UsePrivateConstructor<T>(BsonClassMap<T> cm)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var ctor in typeof(T).GetConstructors(flags).Where(c => c.GetParameters().Length > 0))
            cm.UnmapConstructor(ctor);
        cm.MapConstructor(typeof(T).GetConstructor(flags, Type.EmptyTypes)
                          ?? throw new InvalidOperationException($"{typeof(T).Name} needs a parameterless constructor"));
    }

    /// <summary>DTO, у які проєктуються документи: рядкові Id читаються з BSON ObjectId.</summary>
    private static void RegisterReadModels()
    {
        var objectIdAsString = new StringSerializer(BsonType.ObjectId);

        BsonClassMap.RegisterClassMap<ReviewDto>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(d => d.Id).SetSerializer(objectIdAsString);
        });
        BsonClassMap.RegisterClassMap<ReviewDetailsDto>(cm =>
        {
            cm.AutoMap();
            cm.MapMember(d => d.DiscussionId).SetSerializer(objectIdAsString);
        });
        BsonClassMap.RegisterClassMap<DiscussionDto>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(d => d.Id).SetSerializer(objectIdAsString);
        });
        BsonClassMap.RegisterClassMap<MessagePreviewDto>(cm =>
        {
            cm.AutoMap();
            cm.MapMember(d => d.MessageId).SetSerializer(objectIdAsString);
        });
        BsonClassMap.RegisterClassMap<MessageDto>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(d => d.Id).SetSerializer(objectIdAsString);
            cm.MapMember(d => d.DiscussionId).SetSerializer(objectIdAsString);
            cm.MapMember(d => d.ReplyTo).SetSerializer(objectIdAsString);
        });
    }
}
