using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PitakaApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class TrackRecurringTransactionGenerationHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "has_generated_transactions",
                table: "recurring_transactions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false
            );

            // Surviving Transactions prove use, but their absence cannot prove that a
            // pre-migration recurring transaction never generated one: its history may
            // already have been removed. Protect every existing recurring transaction
            // conservatively. The tradeoff is that an unused pre-migration recurring
            // transaction cannot be deleted.
            migrationBuilder.Sql(
                "UPDATE recurring_transactions SET has_generated_transactions = TRUE;"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "has_generated_transactions",
                table: "recurring_transactions"
            );
        }
    }
}
