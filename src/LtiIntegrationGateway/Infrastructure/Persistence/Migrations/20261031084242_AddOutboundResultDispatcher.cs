using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LtiIntegrationGateway.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboundResultDispatcher : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "OutboundOperations",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ChannelRevisionNumber",
                table: "OutboundMessageLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CreatedAtUnixMs",
                table: "OutboundMessageLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryAddress",
                table: "OutboundMessageLogs",
                type: "TEXT",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ErrorMessage",
                table: "OutboundMessageLogs",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboundMessageLogs_DeliveryStatus_CreatedAtUnixMs",
                table: "OutboundMessageLogs",
                columns: new[] { "DeliveryStatus", "CreatedAtUnixMs" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboundMessageLogs_DeliveryStatus_CreatedAtUnixMs",
                table: "OutboundMessageLogs");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "OutboundOperations");

            migrationBuilder.DropColumn(
                name: "ChannelRevisionNumber",
                table: "OutboundMessageLogs");

            migrationBuilder.DropColumn(
                name: "CreatedAtUnixMs",
                table: "OutboundMessageLogs");

            migrationBuilder.DropColumn(
                name: "DeliveryAddress",
                table: "OutboundMessageLogs");

            migrationBuilder.DropColumn(
                name: "ErrorMessage",
                table: "OutboundMessageLogs");
        }
    }
}
