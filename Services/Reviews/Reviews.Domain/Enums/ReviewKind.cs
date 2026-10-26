namespace Reviews.Domain.Enums;

/// <summary>Вид відгуку (гнучка схема з Лаб. 01: у кожного виду свій набір полів).</summary>
public enum ReviewKind
{
    /// <summary>Лише оцінка.</summary>
    Quick = 1,
    /// <summary>Заголовок, текст, фото, коментарі.</summary>
    Full = 2,
    /// <summary>Рецензія критика: видання, плюси й мінуси.</summary>
    Critic = 3
}
