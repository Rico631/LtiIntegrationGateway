using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LtiIntegrationGateway.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IntegrationScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductRoutes_ProductId",
                table: "ProductRoutes");

            migrationBuilder.DropIndex(
                name: "IX_OutboundChannels_Name",
                table: "OutboundChannels");

            migrationBuilder.DropIndex(
                name: "IX_InboundChannels_Name",
                table: "InboundChannels");

            migrationBuilder.AddColumn<string>(
                name: "IntegrationId",
                table: "ProductRoutes",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "IntegrationId",
                table: "OutboundChannels",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "IntegrationId",
                table: "InboundChannels",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "FlowInstanceStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IntegrationId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    InstanceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AppliedVersion = table.Column<long>(type: "bigint", nullable: false),
                    ReportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlowInstanceStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Integrations",
                columns: table => new
                {
                    IntegrationId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfigurationVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Integrations", x => x.IntegrationId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductRoutes_IntegrationId_ProductId",
                table: "ProductRoutes",
                columns: new[] { "IntegrationId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboundChannels_IntegrationId_Name",
                table: "OutboundChannels",
                columns: new[] { "IntegrationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboundChannels_IntegrationId_Name",
                table: "InboundChannels",
                columns: new[] { "IntegrationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FlowInstanceStates_IntegrationId_InstanceId",
                table: "FlowInstanceStates",
                columns: new[] { "IntegrationId", "InstanceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FlowInstanceStates");

            migrationBuilder.DropTable(
                name: "Integrations");

            migrationBuilder.DropIndex(
                name: "IX_ProductRoutes_IntegrationId_ProductId",
                table: "ProductRoutes");

            migrationBuilder.DropIndex(
                name: "IX_OutboundChannels_IntegrationId_Name",
                table: "OutboundChannels");

            migrationBuilder.DropIndex(
                name: "IX_InboundChannels_IntegrationId_Name",
                table: "InboundChannels");

            migrationBuilder.DropColumn(
                name: "IntegrationId",
                table: "ProductRoutes");

            migrationBuilder.DropColumn(
                name: "IntegrationId",
                table: "OutboundChannels");

            migrationBuilder.DropColumn(
                name: "IntegrationId",
                table: "InboundChannels");

            migrationBuilder.CreateIndex(
                name: "IX_ProductRoutes_ProductId",
                table: "ProductRoutes",
                column: "ProductId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboundChannels_Name",
                table: "OutboundChannels",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboundChannels_Name",
                table: "InboundChannels",
                column: "Name",
                unique: true);
        }
    }
}
