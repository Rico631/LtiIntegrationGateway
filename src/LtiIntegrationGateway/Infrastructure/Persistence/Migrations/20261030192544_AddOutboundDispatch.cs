using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LtiIntegrationGateway.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboundDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboundMessageLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MessageId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ProductId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ChannelName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    HttpMethod = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    RequestBody = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ResponseBody = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    HttpStatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DeliveryStatus = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
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
                name: "OutboundMessageLogs");

            migrationBuilder.DropTable(
                name: "OutboundOperations");

            migrationBuilder.DropTable(
                name: "ProductRouteChannels");

            migrationBuilder.DropTable(
                name: "ProductRouteQueueAddresses");

            migrationBuilder.DropTable(
                name: "ProductRoutes");
        }
    }
}
