using FluentMigrator;

namespace FreightFlow.RfpApi.Migrations;

[Migration(2, "RFP schema — rfps, lanes, bids, bid_lane_prices, awards, outbox tables")]
public sealed class M002_RfpSchema : Migration
{
    public override void Up()
    {
        Create.Table("rfps")
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("shipper_id").AsGuid().NotNullable()
            .WithColumn("status").AsInt32().NotNullable()
            .WithColumn("open_at").AsCustom("timestamptz").NotNullable()
            .WithColumn("close_at").AsCustom("timestamptz").NotNullable()
            .WithColumn("max_bid_rounds").AsInt32().NotNullable()
            .WithColumn("created_at").AsCustom("timestamptz").NotNullable()
            .WithColumn("updated_at").AsCustom("timestamptz").NotNullable();

        Create.Table("lanes")
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("rfp_id").AsGuid().NotNullable()
                .ForeignKey("fk_lanes_rfp", "rfps", "id")
            .WithColumn("origin_zip").AsString(5).NotNullable()
            .WithColumn("dest_zip").AsString(5).NotNullable()
            .WithColumn("freight_class").AsInt32().NotNullable()
            .WithColumn("volume").AsInt32().NotNullable();

        Create.Table("bids")
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("rfp_id").AsGuid().NotNullable()
                .ForeignKey("fk_bids_rfp", "rfps", "id")
            .WithColumn("carrier_id").AsGuid().NotNullable()
            .WithColumn("round").AsInt32().NotNullable()
            .WithColumn("submitted_at").AsCustom("timestamptz").NotNullable();

        Create.Table("bid_lane_prices")
            .WithColumn("bid_id").AsGuid().NotNullable()
                .ForeignKey("fk_bid_lane_prices_bid", "bids", "id")
            .WithColumn("lane_id").AsGuid().NotNullable()
            .WithColumn("amount").AsCustom("numeric(12,2)").NotNullable()
            .WithColumn("currency").AsString(3).NotNullable();

        Create.PrimaryKey("pk_bid_lane_prices")
            .OnTable("bid_lane_prices")
            .Columns("bid_id", "lane_id");

        // Covering index supports the "lowest bid per lane" query efficiently.
        Create.Index("ix_bid_lane_prices_lane_id_amount")
            .OnTable("bid_lane_prices")
            .OnColumn("lane_id").Ascending()
            .OnColumn("amount").Ascending();

        // One award per RFP — rfp_id is the PK (and FK).
        Create.Table("awards")
            .WithColumn("rfp_id").AsGuid().PrimaryKey()
                .ForeignKey("fk_awards_rfp", "rfps", "id")
            .WithColumn("bid_id").AsGuid().NotNullable()
            .WithColumn("carrier_id").AsGuid().NotNullable()
            .WithColumn("awarded_at").AsCustom("timestamptz").NotNullable();

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
        Delete.Table("awards");
        Delete.Index("ix_bid_lane_prices_lane_id_amount").OnTable("bid_lane_prices");
        Delete.Table("bid_lane_prices");
        Delete.Table("bids");
        Delete.Table("lanes");
        Delete.Table("rfps");
    }
}
