using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LtiIntegrationGateway.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IntegrationRuntimeScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboundOperations_MessageId",
                table: "OutboundOperations");

            migrationBuilder.DropIndex(
                name: "IX_OutboundMessageLogs_ChannelName_CreatedAt",
                table: "OutboundMessageLogs");

            migrationBuilder.DropIndex(
                name: "IX_OutboundMessageLogs_DeliveryStatus_CreatedAt",
                table: "OutboundMessageLogs");

            migrationBuilder.DropIndex(
                name: "IX_InboundMessageLogs_ChannelName_ReceivedAt",
                table: "InboundMessageLogs");

            migrationBuilder.AddColumn<string>(
                name: "IntegrationId",
                table: "OutboundOperations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddColumn<string>(
                name: "IntegrationId",
                table: "OutboundMessageLogs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddColumn<string>(
                name: "IntegrationId",
                table: "InboundMessageLogs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddColumn<string>(
                name: "ApplyError",
                table: "FlowInstanceStates",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CertificateExpiresAt",
                table: "FlowInstanceStates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SecretConfigured",
                table: "FlowInstanceStates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_OutboundOperations_IntegrationId_MessageId",
                table: "OutboundOperations",
                columns: new[] { "IntegrationId", "MessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboundMessageLogs_IntegrationId_ChannelName_CreatedAt",
                table: "OutboundMessageLogs",
                columns: new[] { "IntegrationId", "ChannelName", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundMessageLogs_IntegrationId_DeliveryStatus_CreatedAt",
                table: "OutboundMessageLogs",
                columns: new[] { "IntegrationId", "DeliveryStatus", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InboundMessageLogs_IntegrationId_ChannelName_ReceivedAt",
                table: "InboundMessageLogs",
                columns: new[] { "IntegrationId", "ChannelName", "ReceivedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboundOperations_IntegrationId_MessageId",
                table: "OutboundOperations");

            migrationBuilder.DropIndex(
                name: "IX_OutboundMessageLogs_IntegrationId_ChannelName_CreatedAt",
                table: "OutboundMessageLogs");

            migrationBuilder.DropIndex(
                name: "IX_OutboundMessageLogs_IntegrationId_DeliveryStatus_CreatedAt",
                table: "OutboundMessageLogs");

            migrationBuilder.DropIndex(
                name: "IX_InboundMessageLogs_IntegrationId_ChannelName_ReceivedAt",
                table: "InboundMessageLogs");

            migrationBuilder.DropColumn(
                name: "IntegrationId",
                table: "OutboundOperations");

            migrationBuilder.DropColumn(
                name: "IntegrationId",
                table: "OutboundMessageLogs");

            migrationBuilder.DropColumn(
                name: "IntegrationId",
                table: "InboundMessageLogs");

            migrationBuilder.DropColumn(
                name: "ApplyError",
                table: "FlowInstanceStates");

            migrationBuilder.DropColumn(
                name: "CertificateExpiresAt",
                table: "FlowInstanceStates");

            migrationBuilder.DropColumn(
                name: "SecretConfigured",
                table: "FlowInstanceStates");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundOperations_MessageId",
                table: "OutboundOperations",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboundMessageLogs_ChannelName_CreatedAt",
                table: "OutboundMessageLogs",
                columns: new[] { "ChannelName", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundMessageLogs_DeliveryStatus_CreatedAt",
                table: "OutboundMessageLogs",
                columns: new[] { "DeliveryStatus", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InboundMessageLogs_ChannelName_ReceivedAt",
                table: "InboundMessageLogs",
                columns: new[] { "ChannelName", "ReceivedAt" });
        }
    }
}
