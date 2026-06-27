using Microsoft.EntityFrameworkCore;
using TriageBot.Core.Domain;

namespace TriageBot.Infrastructure.Persistence;

/// <summary>
/// EF Core context for TriageBot, backed by PostgreSQL (Npgsql).
/// Enums are persisted as strings for readability and migration stability (see configuration below).
/// </summary>
public class TriageBotDbContext : DbContext
{
    public TriageBotDbContext(DbContextOptions<TriageBotDbContext> options) : base(options)
    {
    }

    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();
    public DbSet<AgentStep> AgentSteps => Set<AgentStep>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Ticket>(entity =>
        {
            entity.HasKey(t => t.Id);

            entity.Property(t => t.Subject).IsRequired().HasMaxLength(200);
            entity.Property(t => t.RequesterEmail).IsRequired().HasMaxLength(256);
            entity.Property(t => t.Body).IsRequired();

            // Enums stored as readable strings rather than ints.
            entity.Property(t => t.Category).HasConversion<string>().HasMaxLength(32);
            entity.Property(t => t.Urgency).HasConversion<string>().HasMaxLength(16);
            entity.Property(t => t.Status).HasConversion<string>().HasMaxLength(32).IsRequired();

            entity.HasIndex(t => t.Status);

            entity.HasMany(t => t.AgentRuns)
                  .WithOne(r => r.Ticket!)
                  .HasForeignKey(r => r.TicketId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasData(SeedData.Tickets);
        });

        modelBuilder.Entity<AgentRun>(entity =>
        {
            entity.HasKey(r => r.Id);

            entity.Property(r => r.Provider).IsRequired().HasMaxLength(32);
            entity.Property(r => r.Outcome).HasMaxLength(1000);

            entity.HasIndex(r => r.TicketId);

            entity.HasMany(r => r.Steps)
                  .WithOne(s => s.AgentRun!)
                  .HasForeignKey(s => s.AgentRunId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgentStep>(entity =>
        {
            entity.HasKey(s => s.Id);

            entity.Property(s => s.ToolName).HasMaxLength(100);

            entity.HasIndex(s => s.AgentRunId);
            entity.HasIndex(s => new { s.AgentRunId, s.StepIndex });
        });
    }
}
