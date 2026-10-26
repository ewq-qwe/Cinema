using MongoDB.Bson;

namespace Reviews.Domain.Common;

/// <summary>
/// Базова сутність, адаптована для MongoDB: Id = ObjectId (_id документа), час створення/зміни
/// і Version для оптимістичної конкурентності (оновлення лише документа з очікуваною версією).
/// </summary>
public abstract class BaseEntity
{
    public ObjectId Id { get; protected set; } = ObjectId.GenerateNewId();
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; protected set; }
    public int Version { get; protected set; }

    protected void Touch() => UpdatedAt = DateTime.UtcNow;

    /// <summary>Викликає репозиторій перед збереженням: у БД потрапляє версія + 1.</summary>
    public void BumpVersion() => Version++;
}
