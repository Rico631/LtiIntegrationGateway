using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LtiIntegrationGateway.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContractOperationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Enabled",
                table: "ContractOperations",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "RateLimitPermit",
                table: "ContractOperations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RateLimitWindowSeconds",
                table: "ContractOperations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientQueue",
                table: "ContractOperations",
                type: "TEXT",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Enabled",
                table: "ContractOperations");

            migrationBuilder.DropColumn(
                name: "RateLimitPermit",
                table: "ContractOperations");

            migrationBuilder.DropColumn(
                name: "RateLimitWindowSeconds",
                table: "ContractOperations");

            migrationBuilder.DropColumn(
                name: "RecipientQueue",
                table: "ContractOperations");
        }
    }
}
