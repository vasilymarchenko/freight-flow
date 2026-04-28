using FluentMigrator;

namespace FreightFlow.RfpApi.Migrations;

[Migration(3, "Add contract_id to awards — records the ContractId once the award workflow completes")]
public sealed class M003_AwardContractId : Migration
{
    public override void Up()
    {
        Alter.Table("awards")
            .AddColumn("contract_id").AsGuid().Nullable();
    }

    public override void Down()
    {
        Delete.Column("contract_id").FromTable("awards");
    }
}
