using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PitakaApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class DropTransactionIsRecurring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_recurring",
                table: "transactions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Structural revert only. Every restored row comes back false; the
            // per-row fact of whether a transaction was generated is not
            // recoverable from this column and is read from RecurringTransactionId
            // instead (see ADR 0008).
            migrationBuilder.AddColumn<bool>(
                name: "is_recurring",
                table: "transactions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }
    }
}
