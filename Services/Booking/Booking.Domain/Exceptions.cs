namespace Booking.Domain.Exceptions;

/// <summary>Базовий клас доменних винятків: їх бачать і DAL, і BLL; API перетворює їх на HTTP-статуси.</summary>
public abstract class DomainException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Ресурс не знайдено → 404.</summary>
public sealed class NotFoundException(string message, Exception? inner = null) : DomainException(message, inner);

/// <summary>Порушено бізнес-правило: недопустимий перехід статусу, зайняте місце, дубль ключа, конфлікт версій → 409.</summary>
public sealed class BusinessConflictException(string message, Exception? inner = null) : DomainException(message, inner);

/// <summary>Некоректні вхідні дані, які виявила бізнес-валідація → 400.</summary>
public sealed class ValidationException(string message, IReadOnlyDictionary<string, string[]>? errors = null)
    : DomainException(message)
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();
}
