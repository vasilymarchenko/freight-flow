using FreightFlow.RfpApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace FreightFlow.RfpApi.Infrastructure.Persistence;

public sealed class RfpDbContext : DbContext
{
    public RfpDbContext(DbContextOptions<RfpDbContext> options) : base(options) { }

    public DbSet<Rfp>           Rfps           { get; set; } = null!;
    public DbSet<OutboxMessage> OutboxMessages { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RfpDbContext).Assembly);
    }
}
