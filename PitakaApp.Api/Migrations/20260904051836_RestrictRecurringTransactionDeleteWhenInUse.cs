using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PitakaApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class RestrictRecurringTransactionDeleteWhenInUse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_transactions_recurring_transactions_recurring_transaction_id",
                table: "transactions");

            migrationBuilder.AddForeignKey(
                name: "fk_transactions_recurring_transactions_recurring_transaction_id",
                table: "transactions",
                column: "recurring_transaction_id",
                principalTable: "recurring_transactions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_transactions_recurring_transactions_recurring_transaction_id",
                table: "transactions");

            migrationBuilder.AddForeignKey(
                name: "fk_transactions_recurring_transactions_recurring_transaction_id",
                table: "transactions",
                column: "recurring_transaction_id",
                principalTable: "recurring_transactions",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
