namespace Reviews.Infrastructure.Persistence;

/// <summary>
/// Секція "MongoDb" в appsettings.{Environment}.json. Пул з'єднань і таймаути задаються в рядку підключення
/// (maxPoolSize, serverSelectionTimeoutMS). Під Aspire рядок підключення приходить як ConnectionStrings:ReviewsDb.
/// </summary>
public sealed class MongoDbOptions
{
    public const string SectionName = "MongoDb";

    public string ConnectionString { get; set; } = "";
    public string DatabaseName { get; set; } = "";

    /// <summary>Створювати індекси під час старту (повторне створення наявного індексу безпечне).</summary>
    public bool CreateIndexesOnStartup { get; set; } = true;

    /// <summary>Наповнювати порожні колекції тестовими даними під час старту (у Production — false).</summary>
    public bool SeedOnStartup { get; set; } = true;
}
