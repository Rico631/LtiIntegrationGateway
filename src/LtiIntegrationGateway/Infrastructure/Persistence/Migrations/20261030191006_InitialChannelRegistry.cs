using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LtiIntegrationGateway.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialChannelRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChannelAuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChannelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RevisionNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    Operation = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Details = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelAuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Channels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Channels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChannelRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChannelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    AuthType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ConfigKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    HeaderName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    PerAttemptTimeoutMs = table.Column<int>(type: "INTEGER", nullable: false),
                    OverallCapMs = table.Column<int>(type: "INTEGER", nullable: false),
                    RetryPolicyJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelRevisions_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "IX_ChannelAllowedMethods_ChannelRevisionId_Method",
                table: "ChannelAllowedMethods",
                columns: new[] { "ChannelRevisionId", "Method" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelAllowedPaths_ChannelRevisionId_Template",
                table: "ChannelAllowedPaths",
                columns: new[] { "ChannelRevisionId", "Template" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelAuditEntries_ChannelId_OccurredAt",
                table: "ChannelAuditEntries",
                columns: new[] { "ChannelId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelRevisions_ChannelId_Number",
                table: "ChannelRevisions",
                columns: new[] { "ChannelId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelRevisions_ChannelId_Status",
                table: "ChannelRevisions",
                columns: new[] { "ChannelId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Channels_Name",
                table: "Channels",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelAllowedMethods");

            migrationBuilder.DropTable(
                name: "ChannelAllowedPaths");

            migrationBuilder.DropTable(
                name: "ChannelAuditEntries");

            migrationBuilder.DropTable(
                name: "ChannelRevisions");

            migrationBuilder.DropTable(
                name: "Channels");
        }
    }
}
