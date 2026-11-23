using Catalog.Dal.Abstractions;
using Catalog.Dal.IntegrationEventLog;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Dal.Repositories;

/// <summary>Outbox: запис подій у той самий DbContext (і транзакцію), вибір неопублікованих для фонового процесу.</summary>
public sealed class IntegrationEventLogRepository(CatalogDbContext context) : IIntegrationEventLogRepository
{
    public void Add(IntegrationEventLogEntry entry) => context.Set<IntegrationEventLogEntry>().Add(entry);

    public async Task<IReadOnlyList<IntegrationEventLogEntry>> GetPendingAsync(DateTime createdBefore, int take, CancellationToken ct = default) =>
        await context.Set<IntegrationEventLogEntry>()
            .Where(e => e.State == EventState.NotPublished && e.OccurredAt <= createdBefore)
            .OrderBy(e => e.OccurredAt)
            .Take(take)
            .ToListAsync(ct);
}
