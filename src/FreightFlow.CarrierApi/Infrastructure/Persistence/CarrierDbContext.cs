using FreightFlow.CarrierApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace FreightFlow.CarrierApi.Infrastructure.Persistence;

public sealed class CarrierDbContext : DbContext
{
    public CarrierDbContext(DbContextOptions<CarrierDbContext> options) : base(options) { }

    public DbSet<Carrier>       Carriers       { get; set; } = null!;
    public DbSet<OutboxMessage> OutboxMessages { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CarrierDbContext).Assembly);
    }
}
