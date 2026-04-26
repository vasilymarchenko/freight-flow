using FluentMigrator;

namespace FreightFlow.RfpApi.Migrations;

[Migration(1, "Initial schema — placeholder; full schema added in Milestone 3")]
public sealed class M001_InitialSchema : Migration
{
    public override void Up()
    {
        // Full RFP schema defined in Milestone 3.
        // This migration exists to verify the FluentMigrator runner is wired correctly.
    }

    public override void Down()
    {
        // No-op: nothing was created in Up().
    }
}
