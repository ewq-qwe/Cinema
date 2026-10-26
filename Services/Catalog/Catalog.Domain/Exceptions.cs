namespace Catalog.Domain.Exceptions;

/// <summary>Порушення інваріанта сутності (бізнес-правило самої моделі) → 409.</summary>
public class DomainException(string message) : Exception(message);

/// <summary>Ресурс не знайдено → 404.</summary>
public sealed class EntityNotFoundException(string message) : Exception(message)
{
    public static EntityNotFoundException For<T>(object id) => new($"{typeof(T).Name} {id} не знайдено.");
}

/// <summary>Конфлікт із даними в БД (дубль, конфлікт версій, залежні записи) → 409.</summary>
public sealed class ConflictException(string message) : Exception(message);

/// <summary>Некоректні дані, виявлені бізнес-валідацією зі станом БД → 400.</summary>
public sealed class ValidationException(string message, IReadOnlyDictionary<string, string[]>? errors = null) : Exception(message)
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();

    public static ValidationException For(string field, string error) =>
        new(error, new Dictionary<string, string[]> { [field] = [error] });
}
