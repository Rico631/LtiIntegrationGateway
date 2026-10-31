using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LtiIntegrationGateway.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboundErrorIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_OutboundMessageLogs_ChannelName_CreatedAtUnixMs",
                table: "OutboundMessageLogs",
                columns: new[] { "ChannelName", "CreatedAtUnixMs" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboundMessageLogs_ChannelName_CreatedAtUnixMs",
                table: "OutboundMessageLogs");
        }
    }
}
