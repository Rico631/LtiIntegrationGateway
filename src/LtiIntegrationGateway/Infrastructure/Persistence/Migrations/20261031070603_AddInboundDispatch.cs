using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LtiIntegrationGateway.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInboundDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthType",
                table: "ContractVersions",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ConfigKey",
                table: "ContractVersions",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HeaderName",
                table: "ContractVersions",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RateLimitPermit",
                table: "ContractVersions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RateLimitWindowSeconds",
                table: "ContractVersions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientQueue",
                table: "ContractVersions",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserName",
                table: "ContractVersions",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestSchemas",
                table: "ContractOperations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InboundMessageLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MessageId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ChannelName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ContractVersionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    HttpMethod = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    RouteTemplate = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    OperationId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    RequestBody = table.Column<byte[]>(type: "BLOB", nullable: false),
                    QueryParameters = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    DeliveryStatus = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboundMessageLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboundMessageLogs_ChannelName_ReceivedAt",
                table: "InboundMessageLogs",
                columns: new[] { "ChannelName", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InboundMessageLogs_MessageId",
                table: "InboundMessageLogs",
                column: "MessageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboundMessageLogs");

            migrationBuilder.DropColumn(
                name: "AuthType",
                table: "ContractVersions");

            migrationBuilder.DropColumn(
                name: "ConfigKey",
                table: "ContractVersions");

            migrationBuilder.DropColumn(
                name: "HeaderName",
                table: "ContractVersions");

            migrationBuilder.DropColumn(
                name: "RateLimitPermit",
                table: "ContractVersions");

            migrationBuilder.DropColumn(
                name: "RateLimitWindowSeconds",
                table: "ContractVersions");

            migrationBuilder.DropColumn(
                name: "RecipientQueue",
                table: "ContractVersions");

            migrationBuilder.DropColumn(
                name: "UserName",
                table: "ContractVersions");

            migrationBuilder.DropColumn(
                name: "RequestSchemas",
                table: "ContractOperations");
        }
    }
}
