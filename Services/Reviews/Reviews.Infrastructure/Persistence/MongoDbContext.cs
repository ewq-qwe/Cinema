using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Reviews.Domain.Entities;

namespace Reviews.Infrastructure.Persistence;

/// <summary>
/// Точка доступу до бази cinema_reviews: IMongoDatabase за налаштуваннями з IOptions і колекції доменних сутностей.
/// Singleton — MongoClient потокобезпечний і сам керує пулом з'єднань.
/// </summary>
public sealed class MongoDbContext
{
    public const string ReviewsCollection = "reviews";
    public const string DiscussionsCollection = "discussions";
    public const string MessagesCollection = "messages";

    public MongoDbContext(IMongoClient client, IOptions<MongoDbOptions> options)
    {
        Database = client.GetDatabase(options.Value.DatabaseName);
    }

    public IMongoDatabase Database { get; }

    public IMongoCollection<Review> Reviews => Database.GetCollection<Review>(ReviewsCollection);
    public IMongoCollection<Discussion> Discussions => Database.GetCollection<Discussion>(DiscussionsCollection);
    public IMongoCollection<Message> Messages => Database.GetCollection<Message>(MessagesCollection);
}
