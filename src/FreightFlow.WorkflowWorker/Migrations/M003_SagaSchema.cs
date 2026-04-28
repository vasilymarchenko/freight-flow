using FluentMigrator;

namespace FreightFlow.WorkflowWorker.Migrations;

[Migration(3, "Saga schema — award_workflow_state table in saga schema")]
public sealed class M003_SagaSchema : Migration
{
    public override void Up()
    {
        // Create a dedicated schema so saga tables don't collide with rfp tables.
        Execute.Sql("CREATE SCHEMA IF NOT EXISTS saga;");

        Create.Table("award_workflow_state").InSchema("saga")
            .WithColumn("correlation_id").AsGuid().PrimaryKey()
            .WithColumn("current_state").AsString(64).NotNullable()
            .WithColumn("rfp_id").AsGuid().NotNullable()
            .WithColumn("carrier_id").AsGuid().NotNullable()
            .WithColumn("bid_id").AsGuid().NotNullable()
            .WithColumn("lane_id").AsGuid().NotNullable()
            .WithColumn("agreed_amount").AsCustom("numeric(12,2)").NotNullable()
            .WithColumn("agreed_currency").AsString(3).NotNullable()
            .WithColumn("volume_to_reserve").AsInt32().NotNullable()
            .WithColumn("reservation_id").AsGuid().NotNullable()
            .WithColumn("contract_id").AsGuid().Nullable()
            .WithColumn("saga_timeout_token_id").AsGuid().Nullable()
            .WithColumn("created_at").AsCustom("timestamptz").NotNullable()
            .WithColumn("updated_at").AsCustom("timestamptz").NotNullable()
            // MassTransit uses row_version for optimistic concurrency on the saga row.
            .WithColumn("row_version").AsInt32().NotNullable().WithDefaultValue(0);
    }

    public override void Down()
    {
        Delete.Table("award_workflow_state").InSchema("saga");
        Execute.Sql("DROP SCHEMA IF EXISTS saga;");
    }
}
