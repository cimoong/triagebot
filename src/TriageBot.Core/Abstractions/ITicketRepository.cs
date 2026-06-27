using TriageBot.Core.Domain;

namespace TriageBot.Core.Abstractions;

/// <summary>Persistence boundary for tickets. Implemented in the Infrastructure layer (e.g. EF Core).</summary>
public interface ITicketRepository
{
    Task<Ticket?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Ticket>> GetAllAsync(CancellationToken cancellationToken = default);

    Task AddAsync(Ticket ticket, CancellationToken cancellationToken = default);

    Task UpdateAsync(Ticket ticket, CancellationToken cancellationToken = default);
}
