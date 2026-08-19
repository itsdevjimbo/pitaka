using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PitakaApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountIdToGoalContribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_goal_contributions_transactions_transaction_id",
                table: "goal_contributions");

            migrationBuilder.AddColumn<int>(
                name: "account_id",
                table: "goal_contributions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_goal_contributions_account_id",
                table: "goal_contributions",
                column: "account_id");

            migrationBuilder.AddForeignKey(
                name: "fk_goal_contributions_accounts_account_id",
                table: "goal_contributions",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_goal_contributions_transactions_transaction_id",
                table: "goal_contributions",
                column: "transaction_id",
                principalTable: "transactions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_goal_contributions_accounts_account_id",
                table: "goal_contributions");

            migrationBuilder.DropForeignKey(
                name: "fk_goal_contributions_transactions_transaction_id",
                table: "goal_contributions");

            migrationBuilder.DropIndex(
                name: "ix_goal_contributions_account_id",
                table: "goal_contributions");

            migrationBuilder.DropColumn(
                name: "account_id",
                table: "goal_contributions");

            migrationBuilder.AddForeignKey(
                name: "fk_goal_contributions_transactions_transaction_id",
                table: "goal_contributions",
                column: "transaction_id",
                principalTable: "transactions",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
