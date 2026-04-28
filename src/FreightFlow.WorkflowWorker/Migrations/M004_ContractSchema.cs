using FluentMigrator;

namespace FreightFlow.WorkflowWorker.Migrations;

[Migration(4, "Contract schema — contracts table for the WorkflowWorker Contract aggregate")]
public sealed class M004_ContractSchema : Migration
{
    public override void Up()
    {
        Create.Table("contracts")
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("rfp_id").AsGuid().NotNullable()
            .WithColumn("carrier_id").AsGuid().NotNullable()
            .WithColumn("lane_id").AsGuid().NotNullable()
            .WithColumn("agreed_amount").AsCustom("numeric(12,2)").NotNullable()
            .WithColumn("agreed_currency").AsString(3).NotNullable()
            .WithColumn("issued_at").AsCustom("timestamptz").NotNullable()
            .WithColumn("status").AsInt32().NotNullable();

        // Fast lookup by rfp_id (used when verifying contract issuance).
        Create.Index("ix_contracts_rfp_id")
            .OnTable("contracts")
            .OnColumn("rfp_id").Ascending();
    }

    public override void Down()
    {
        Delete.Index("ix_contracts_rfp_id").OnTable("contracts");
        Delete.Table("contracts");
    }
}
