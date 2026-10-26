namespace Reviews.Domain.Exceptions;

/// <summary>Порушення бізнес-правила домену (→ 400).</summary>
public class DomainException(string message) : Exception(message);

/// <summary>Документ не знайдено (→ 404).</summary>
public class NotFoundException(string message) : Exception(message)
{
    public static NotFoundException For(string entity, object id) => new($"{entity} з Id '{id}' не знайдено.");
}

/// <summary>Конфлікт стану: документ змінив хтось інший, або дублікат (→ 409).</summary>
public class ConflictException(string message) : Exception(message);
