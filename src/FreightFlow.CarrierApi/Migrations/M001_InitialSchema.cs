using FluentMigrator;

namespace FreightFlow.CarrierApi.Migrations;

[Migration(1, "Initial schema — placeholder; full schema added in Milestone 2")]
public sealed class M001_InitialSchema : Migration
{
    public override void Up()
    {
        // Full carrier schema defined in Milestone 2.
        // This migration exists to verify the FluentMigrator runner is wired correctly.
    }

    public override void Down()
    {
        // No-op: nothing was created in Up().
    }
}
