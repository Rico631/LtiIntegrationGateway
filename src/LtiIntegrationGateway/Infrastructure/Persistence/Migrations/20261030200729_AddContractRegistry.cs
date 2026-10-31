using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LtiIntegrationGateway.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContractRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContractVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChannelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceFormat = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Document = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractVersions_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ManagementAuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Operation = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Target = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Result = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Details = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagementAuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContractOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContractVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HttpMethod = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Template = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    OperationId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractOperations_ContractVersions_ContractVersionId",
                        column: x => x.ContractVersionId,
                        principalTable: "ContractVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContractOperations_ContractVersionId_HttpMethod_Template",
                table: "ContractOperations",
                columns: new[] { "ContractVersionId", "HttpMethod", "Template" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractVersions_ChannelId_Status",
                table: "ContractVersions",
                columns: new[] { "ChannelId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractVersions_ChannelId_Version",
                table: "ContractVersions",
                columns: new[] { "ChannelId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManagementAuditEntries_Operation",
                table: "ManagementAuditEntries",
                column: "Operation");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractOperations");

            migrationBuilder.DropTable(
                name: "ManagementAuditEntries");

            migrationBuilder.DropTable(
                name: "ContractVersions");
        }
    }
}
