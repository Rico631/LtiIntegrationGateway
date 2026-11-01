using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LtiIntegrationGateway.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChannelAuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
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
                name: "InboundChannels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ResponseMode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    InboundResponseTimeoutMs = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboundChannels", x => x.Id);
                });

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
                    ResponseStatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    ResponseContentType = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ResponseBody = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ResponseHeaders = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: true),
                    ReceivedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboundMessageLogs", x => x.Id);
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
                name: "OutboundChannels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboundChannels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboundMessageLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MessageId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ProductId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ChannelName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DeliveryAddress = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    HttpMethod = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    RequestBody = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ResponseBody = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    HttpStatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ChannelRevisionNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DeliveryStatus = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboundMessageLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboundOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MessageId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ProductId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ChannelName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    HttpStatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ReturnAddress = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboundOperations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductRoutes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductRoutes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContractVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InboundChannelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceFormat = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Document = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    RecipientQueue = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    AuthType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ConfigKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    HeaderName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    RateLimitPermit = table.Column<int>(type: "INTEGER", nullable: true),
                    RateLimitWindowSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractVersions_InboundChannels_InboundChannelId",
                        column: x => x.InboundChannelId,
                        principalTable: "InboundChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChannelRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OutboundChannelId = table.Column<Guid>(type: "TEXT", nullable: false),
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
                        name: "FK_ChannelRevisions_OutboundChannels_OutboundChannelId",
                        column: x => x.OutboundChannelId,
                        principalTable: "OutboundChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductRouteChannels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductRouteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChannelName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductRouteChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductRouteChannels_ProductRoutes_ProductRouteId",
                        column: x => x.ProductRouteId,
                        principalTable: "ProductRoutes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductRouteQueueAddresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductRouteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductRouteQueueAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductRouteQueueAddresses_ProductRoutes_ProductRouteId",
                        column: x => x.ProductRouteId,
                        principalTable: "ProductRoutes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContractOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContractVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HttpMethod = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Template = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    OperationId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    RequestSchemas = table.Column<string>(type: "TEXT", nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    RecipientQueue = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
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
                name: "IX_ChannelAuditEntries_Direction_ChannelId_OccurredAt",
                table: "ChannelAuditEntries",
                columns: new[] { "Direction", "ChannelId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelRevisions_OutboundChannelId_Number",
                table: "ChannelRevisions",
                columns: new[] { "OutboundChannelId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelRevisions_OutboundChannelId_Status",
                table: "ChannelRevisions",
                columns: new[] { "OutboundChannelId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractOperations_ContractVersionId_HttpMethod_Template",
                table: "ContractOperations",
                columns: new[] { "ContractVersionId", "HttpMethod", "Template" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractVersions_InboundChannelId_Status",
                table: "ContractVersions",
                columns: new[] { "InboundChannelId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractVersions_InboundChannelId_Version",
                table: "ContractVersions",
                columns: new[] { "InboundChannelId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboundChannels_Name",
                table: "InboundChannels",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboundMessageLogs_ChannelName_ReceivedAt",
                table: "InboundMessageLogs",
                columns: new[] { "ChannelName", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InboundMessageLogs_MessageId",
                table: "InboundMessageLogs",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ManagementAuditEntries_Operation",
                table: "ManagementAuditEntries",
                column: "Operation");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundChannels_Name",
                table: "OutboundChannels",
                column: "Name",
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
                name: "IX_OutboundMessageLogs_MessageId_DeliveryStatus",
                table: "OutboundMessageLogs",
                columns: new[] { "MessageId", "DeliveryStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundOperations_MessageId",
                table: "OutboundOperations",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductRouteChannels_ProductRouteId_ChannelName",
                table: "ProductRouteChannels",
                columns: new[] { "ProductRouteId", "ChannelName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductRouteQueueAddresses_ProductRouteId_Address",
                table: "ProductRouteQueueAddresses",
                columns: new[] { "ProductRouteId", "Address" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductRoutes_ProductId",
                table: "ProductRoutes",
                column: "ProductId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelAuditEntries");

            migrationBuilder.DropTable(
                name: "ChannelRevisions");

            migrationBuilder.DropTable(
                name: "ContractOperations");

            migrationBuilder.DropTable(
                name: "InboundMessageLogs");

            migrationBuilder.DropTable(
                name: "ManagementAuditEntries");

            migrationBuilder.DropTable(
                name: "OutboundMessageLogs");

            migrationBuilder.DropTable(
                name: "OutboundOperations");

            migrationBuilder.DropTable(
                name: "ProductRouteChannels");

            migrationBuilder.DropTable(
                name: "ProductRouteQueueAddresses");

            migrationBuilder.DropTable(
                name: "OutboundChannels");

            migrationBuilder.DropTable(
                name: "ContractVersions");

            migrationBuilder.DropTable(
                name: "ProductRoutes");

            migrationBuilder.DropTable(
                name: "InboundChannels");
        }
    }
}
