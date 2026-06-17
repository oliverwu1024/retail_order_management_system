using Microsoft.EntityFrameworkCore;
using Retail.Api.Common.Models;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;

namespace Retail.Api.Services;

/// <summary>
/// Read-only search over the <see cref="AuditLog"/> table. Injects the DbContext directly — the
/// audit log is a technical, append-only read model (no repository / domain rules), so the query
/// lives here. Filters are exact-match on the indexed columns (the three IX_AuditLog_* indexes back
/// actor / entity / time searches).
/// </summary>
public sealed class AuditQueryService : IAuditQueryService
{
    private readonly RetailDbContext _db;

    public AuditQueryService(RetailDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<PagedResult<AuditLogDto>> SearchAsync(AuditLogListQuery query, CancellationToken ct)
    {
        int safePage = query.Page < 1 ? 1 : query.Page;
        int safeSize = Math.Clamp(query.PageSize, 1, 100);

        IQueryable<AuditLog> rows = _db.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Actor))
        {
            string actor = query.Actor.Trim();
            rows = rows.Where(a => a.Actor == actor);
        }
        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            string entityType = query.EntityType.Trim();
            rows = rows.Where(a => a.EntityType == entityType);
        }
        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            string entityId = query.EntityId.Trim();
            rows = rows.Where(a => a.EntityId == entityId);
        }
        if (query.From is DateTimeOffset from)
        {
            rows = rows.Where(a => a.OccurredAt >= from);
        }
        if (query.To is DateTimeOffset to)
        {
            rows = rows.Where(a => a.OccurredAt < to);
        }

        int total = await rows.CountAsync(ct);
        List<AuditLog> page = await rows
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .Skip((safePage - 1) * safeSize)
            .Take(safeSize)
            .ToListAsync(ct);

        var dtos = page
            .Select(a => new AuditLogDto(
                a.Id, a.Actor, a.Action, a.EntityType, a.EntityId, a.BeforeJson, a.AfterJson, a.OccurredAt))
            .ToList();

        return new PagedResult<AuditLogDto>(dtos, total, safePage, safeSize);
    }
}
