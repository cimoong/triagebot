using System.Collections.Concurrent;
using TriageBot.Core.Abstractions;
using TriageBot.Core.Domain;

namespace TriageBot.Infrastructure.Persistence;

/// <summary>
/// Placeholder repository backed by an in-memory store, so the workflow runs end-to-end
/// before EF Core / a real database is introduced. Swapped behind <see cref="ITicketRepository"/>.
/// </summary>
public sealed class InMemoryTicketRepository : ITicketRepository
{
    private readonly ConcurrentDictionary<Guid, Ticket> _store = new();

    public Task<Ticket?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.GetValueOrDefault(id));

    public Task<IReadOnlyList<Ticket>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult((IReadOnlyList<Ticket>)_store.Values.OrderByDescending(t => t.CreatedAtUtc).ToList());

    public Task AddAsync(Ticket ticket, CancellationToken cancellationToken = default)
    {
        _store[ticket.Id] = ticket;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Ticket ticket, CancellationToken cancellationToken = default)
    {
        ticket.UpdatedAtUtc = DateTime.UtcNow;
        _store[ticket.Id] = ticket;
        return Task.CompletedTask;
    }
}
