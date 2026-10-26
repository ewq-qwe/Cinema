using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MongoDB.Driver.Core.Configuration;
using Reviews.Application.Common.Interfaces;
using Reviews.Domain.Interfaces;
using Reviews.Infrastructure.Persistence;
using Reviews.Infrastructure.Persistence.Queries;
using Reviews.Infrastructure.Persistence.Repositories;
using Reviews.Infrastructure.Seeding;

namespace Reviews.Infrastructure;

public static class DependencyInjection
{
    /// <summary>MongoDB: BSON-мапінг, налаштування, клієнт, контекст, репозиторії, запити, індекси й сідінг.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        MongoMappings.Register(); // один раз, до першого звернення до БД

        services.AddOptions<MongoDbOptions>()
            .Bind(config.GetSection(MongoDbOptions.SectionName))
            .PostConfigure(o =>
            {
                // Під Aspire рядок підключення приходить як ConnectionStrings:ReviewsDb і має пріоритет
                var aspire = config.GetConnectionString("ReviewsDb");
                if (!string.IsNullOrWhiteSpace(aspire)) o.ConnectionString = aspire;
            })
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), "MongoDb:ConnectionString is required")
            .Validate(o => !string.IsNullOrWhiteSpace(o.DatabaseName), "MongoDb:DatabaseName is required")
            .ValidateOnStart();

        services.AddSingleton<IMongoClient>(sp =>
        {
            var settings = MongoClientSettings.FromConnectionString(
                sp.GetRequiredService<IOptions<MongoDbOptions>>().Value.ConnectionString);
            // Логи драйвера (категорії MongoDB.*): напр. "MongoDB.Command": "Debug" покаже кожну команду до БД
            settings.LoggingSettings = new LoggingSettings(sp.GetRequiredService<ILoggerFactory>());
            return new MongoClient(settings);
        });
        services.AddSingleton<MongoDbContext>();

        services.AddScoped<IReviewRepository, ReviewRepository>();
        services.AddScoped<IDiscussionRepository, DiscussionRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IReviewQueries, ReviewQueries>();
        services.AddScoped<IDiscussionQueries, DiscussionQueries>();

        // Порядок: спершу індекси (унікальний індекс має існувати до вставки даних), потім сідінг
        services.AddSingleton<IDataSeeder, DiscussionSeeder>();
        services.AddSingleton<IDataSeeder, MessageSeeder>();
        services.AddSingleton<IDataSeeder, ReviewSeeder>();
        services.AddHostedService<IndexCreationService>();
        services.AddHostedService<DataSeedingService>();

        return services;
    }
}
