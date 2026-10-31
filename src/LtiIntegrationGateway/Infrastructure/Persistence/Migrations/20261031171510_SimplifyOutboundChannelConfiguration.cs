using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LtiIntegrationGateway.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyOutboundChannelConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelAllowedMethods");

            migrationBuilder.DropTable(
                name: "ChannelAllowedPaths");

            migrationBuilder.DropIndex(
                name: "IX_OutboundMessageLogs_ChannelName_CreatedAtUnixMs",
                table: "OutboundMessageLogs");

            migrationBuilder.DropIndex(
                name: "IX_OutboundMessageLogs_DeliveryStatus_CreatedAtUnixMs",
                table: "OutboundMessageLogs");

            migrationBuilder.DropIndex(
                name: "IX_InboundMessageLogs_ChannelName_ReceivedAtUnixMs",
                table: "InboundMessageLogs");

            // Переносим значения времени из Unix-колонок до изменения типа.
            migrationBuilder.Sql(
                "UPDATE \"OutboundMessageLogs\" SET \"CreatedAt\" = CAST(\"CreatedAtUnixMs\" AS TEXT);");
            migrationBuilder.Sql(
                "UPDATE \"InboundMessageLogs\" SET \"ReceivedAt\" = CAST(\"ReceivedAtUnixMs\" AS TEXT);");

            migrationBuilder.DropColumn(
                name: "CreatedAtUnixMs",
                table: "OutboundMessageLogs");

            migrationBuilder.DropColumn(
                name: "ReceivedAtUnixMs",
                table: "InboundMessageLogs");

            migrationBuilder.DropColumn(
                name: "RateLimitPermit",
                table: "ContractOperations");

            migrationBuilder.DropColumn(
                name: "RateLimitWindowSeconds",
                table: "ContractOperations");

            migrationBuilder.AlterColumn<long>(
                name: "CreatedAt",
                table: "OutboundMessageLogs",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<long>(
                name: "ReceivedAt",
                table: "InboundMessageLogs",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "TEXT");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundMessageLogs_ChannelName_CreatedAt",
                table: "OutboundMessageLogs",
                columns: new[] { "ChannelName", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundMessageLogs_DeliveryStatus_CreatedAt",
                table: "OutboundMessageLogs",
                columns: new[] { "DeliveryStatus", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboundMessageLogs_ChannelName_CreatedAt",
                table: "OutboundMessageLogs");

            migrationBuilder.DropIndex(
                name: "IX_OutboundMessageLogs_DeliveryStatus_CreatedAt",
                table: "OutboundMessageLogs");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "OutboundMessageLogs",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<long>(
                name: "CreatedAtUnixMs",
                table: "OutboundMessageLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ReceivedAt",
                table: "InboundMessageLogs",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<long>(
                name: "ReceivedAtUnixMs",
                table: "InboundMessageLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "RateLimitPermit",
                table: "ContractOperations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RateLimitWindowSeconds",
                table: "ContractOperations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChannelAllowedMethods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChannelRevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelAllowedMethods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelAllowedMethods_ChannelRevisions_ChannelRevisionId",
                        column: x => x.ChannelRevisionId,
                        principalTable: "ChannelRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChannelAllowedPaths",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChannelRevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Template = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelAllowedPaths", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelAllowedPaths_ChannelRevisions_ChannelRevisionId",
                        column: x => x.ChannelRevisionId,
                        principalTable: "ChannelRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundMessageLogs_ChannelName_CreatedAtUnixMs",
                table: "OutboundMessageLogs",
                columns: new[] { "ChannelName", "CreatedAtUnixMs" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundMessageLogs_DeliveryStatus_CreatedAtUnixMs",
                table: "OutboundMessageLogs",
                columns: new[] { "DeliveryStatus", "CreatedAtUnixMs" });

            migrationBuilder.CreateIndex(
                name: "IX_InboundMessageLogs_ChannelName_ReceivedAtUnixMs",
                table: "InboundMessageLogs",
                columns: new[] { "ChannelName", "ReceivedAtUnixMs" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelAllowedMethods_ChannelRevisionId_Method",
                table: "ChannelAllowedMethods",
                columns: new[] { "ChannelRevisionId", "Method" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelAllowedPaths_ChannelRevisionId_Template",
                table: "ChannelAllowedPaths",
                columns: new[] { "ChannelRevisionId", "Template" },
                unique: true);
        }
    }
}
