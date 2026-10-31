using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LtiIntegrationGateway.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInboundReceivedAtUnixMs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ReceivedAtUnixMs",
                table: "InboundMessageLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            // Backfill существующих записей: SQLite разбирает сохранённый DateTimeOffset как текст
            // ("yyyy-MM-dd HH:mm:ss[.fffffff]±HH:MM") и переводит его в UTC по смещению. strftime('%s')
            // даёт целые секунды, поэтому у дорелизных строк точность до секунды; новые записи
            // заполняют колонку из DateTimeOffset с миллисекундами. Строки с неразбираемым значением
            // остаются с 0 и не нарушают NOT NULL.
            migrationBuilder.Sql(
                "UPDATE \"InboundMessageLogs\" " +
                "SET \"ReceivedAtUnixMs\" = CAST(strftime('%s', \"ReceivedAt\") AS INTEGER) * 1000 " +
                "WHERE strftime('%s', \"ReceivedAt\") IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_InboundMessageLogs_ChannelName_ReceivedAtUnixMs",
                table: "InboundMessageLogs",
                columns: new[] { "ChannelName", "ReceivedAtUnixMs" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InboundMessageLogs_ChannelName_ReceivedAtUnixMs",
                table: "InboundMessageLogs");

            migrationBuilder.DropColumn(
                name: "ReceivedAtUnixMs",
                table: "InboundMessageLogs");
        }
    }
}
