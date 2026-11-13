using Dms.Documents.Application;
using Dms.Documents.Contracts;
using Dms.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace Dms.Documents.Infrastructure.Persistence;

public sealed class LegacyExtractionRepository(DocumentsDbContext context) : ILegacyExtractionRepository
{
    public Task<LegacyExtraction?> FindAsync(LegacyExtractionId id, CancellationToken cancellationToken) =>
        context.LegacyExtractions.FirstOrDefaultAsync(extraction => extraction.Id == id, cancellationToken);

    public async Task<IReadOnlyList<LegacyExtraction>> ListAsync(int take, CancellationToken cancellationToken) =>
        await context.LegacyExtractions.AsNoTracking()
            .OrderByDescending(extraction => extraction.CreatedAt)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(cancellationToken);

    public void Add(LegacyExtraction extraction) => context.LegacyExtractions.Add(extraction);
}
