using FreightFlow.WorkflowWorker.Application;
using FreightFlow.WorkflowWorker.Domain;
using Microsoft.EntityFrameworkCore;

namespace FreightFlow.WorkflowWorker.Infrastructure.Persistence;

public sealed class WorkflowDbContext : DbContext
{
    public WorkflowDbContext(DbContextOptions<WorkflowDbContext> options) : base(options) { }

    public DbSet<AwardWorkflowState> AwardWorkflowStates { get; set; } = null!;
    public DbSet<Contract>           Contracts           { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WorkflowDbContext).Assembly);
    }
}
