using FluentMigrator;

namespace FreightFlow.CarrierApi.Migrations;

[Migration(2, "Carrier schema — carriers, capacity_records, outbox tables")]
public sealed class M002_CarrierSchema : Migration
{
    public override void Up()
    {
        Create.Table("carriers")
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("dot_number").AsString().NotNullable().Unique()
            .WithColumn("name").AsString(200).NotNullable()
            .WithColumn("authority_status").AsInt32().NotNullable()
            .WithColumn("insurance_expiry").AsDate().NotNullable()
            .WithColumn("profile").AsCustom("jsonb").NotNullable()
            .WithColumn("created_at").AsCustom("timestamptz").NotNullable();

        Create.Table("capacity_records")
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("carrier_id").AsGuid().NotNullable()
                .ForeignKey("fk_capacity_records_carrier", "carriers", "id")
            .WithColumn("lane_id").AsGuid().NotNullable()
            .WithColumn("available_volume").AsInt32().NotNullable()
            .WithColumn("reserved_volume").AsInt32().NotNullable().WithDefaultValue(0);

        Create.Table("outbox")
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("message_type").AsString(500).NotNullable()
            .WithColumn("payload").AsCustom("jsonb").NotNullable()
            .WithColumn("created_at").AsCustom("timestamptz").NotNullable()
            .WithColumn("sent_at").AsCustom("timestamptz").Nullable();

        Create.Index("ix_outbox_sent_at")
            .OnTable("outbox")
            .OnColumn("sent_at")
            .Ascending();
    }

    public override void Down()
    {
        Delete.Index("ix_outbox_sent_at").OnTable("outbox");
        Delete.Table("outbox");
        Delete.Table("capacity_records");
        Delete.Table("carriers");
    }
}
