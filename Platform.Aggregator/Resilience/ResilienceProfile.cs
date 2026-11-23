namespace Platform.Aggregator.Resilience;

/// <summary>
/// Параметри resilience для одного сервісу-залежності. Обґрунтування значень — README, розділ «Лаб. 07».
/// Узгодженість таймаутів: TotalTimeout ≥ AttemptTimeout × (MaxRetryAttempts + 1) + затримки між спробами
/// (інакше повтори не встигнуть виконатися); SamplingDuration ≥ 2 × AttemptTimeout (вимога валідації Polly).
/// </summary>
public sealed record ResilienceProfile(
    string Name,
    int MaxRetryAttempts,
    TimeSpan RetryDelay,
    TimeSpan AttemptTimeout,
    TimeSpan TotalTimeout,
    double FailureRatio,
    int MinimumThroughput,
    TimeSpan SamplingDuration,
    TimeSpan BreakDuration,
    int GrpcMaxAttempts,
    TimeSpan GrpcInitialBackoff)
{
    /// <summary>
    /// Каталог — КРИТИЧНИЙ: без фільму картку не зібрати (для картки фільму це основний ресурс).
    /// Більше повторів, але чутливіший запобіжник: якщо Каталог систематично падає, краще швидко повернути 503,
    /// ніж тримати користувача й навантажувати хворий сервіс.
    /// </summary>
    public static readonly ResilienceProfile Critical = new(
        Name: "catalog-critical",
        MaxRetryAttempts: 4, RetryDelay: TimeSpan.FromMilliseconds(200),
        AttemptTimeout: TimeSpan.FromSeconds(2), TotalTimeout: TimeSpan.FromSeconds(12),
        FailureRatio: 0.3, MinimumThroughput: 10,
        SamplingDuration: TimeSpan.FromSeconds(30), BreakDuration: TimeSpan.FromSeconds(15),
        GrpcMaxAttempts: 4, GrpcInitialBackoff: TimeSpan.FromMilliseconds(200));

    /// <summary>
    /// Бронювання — ОСНОВНИЙ ресурс картки бронювання, але й джерело розкладу для картки фільму.
    /// Помірні значення (близькі до стандартних), лише з меншим MinimumThroughput для навчального стенду.
    /// </summary>
    public static readonly ResilienceProfile Standard = new(
        Name: "booking-standard",
        MaxRetryAttempts: 3, RetryDelay: TimeSpan.FromMilliseconds(300),
        AttemptTimeout: TimeSpan.FromSeconds(3), TotalTimeout: TimeSpan.FromSeconds(15),
        FailureRatio: 0.5, MinimumThroughput: 10,
        SamplingDuration: TimeSpan.FromSeconds(30), BreakDuration: TimeSpan.FromSeconds(10),
        GrpcMaxAttempts: 3, GrpcInitialBackoff: TimeSpan.FromMilliseconds(300));

    /// <summary>
    /// Відгуки — НЕКРИТИЧНІ: без них Aggregator поверне часткову відповідь (Лаб. 05).
    /// Мінімум повторів і короткі таймаути — швидко здатися; толерантніший запобіжник (FailureRatio 0.7),
    /// щоб поодинокі збої не відрізали відгуки зовсім, але серія відмов — розмикала ланцюг і давала fail-fast.
    /// </summary>
    public static readonly ResilienceProfile NonCritical = new(
        Name: "reviews-noncritical",
        MaxRetryAttempts: 1, RetryDelay: TimeSpan.FromMilliseconds(200),
        AttemptTimeout: TimeSpan.FromSeconds(1), TotalTimeout: TimeSpan.FromSeconds(3),
        FailureRatio: 0.7, MinimumThroughput: 6,
        SamplingDuration: TimeSpan.FromSeconds(20), BreakDuration: TimeSpan.FromSeconds(15),
        GrpcMaxAttempts: 2, GrpcInitialBackoff: TimeSpan.FromMilliseconds(200));
}
