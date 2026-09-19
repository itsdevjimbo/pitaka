using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PitakaApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class PrepareLinkedContributionStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_goal_contributions_transactions_transaction_id",
                table: "goal_contributions"
            );

            migrationBuilder.DropIndex(
                name: "ix_goal_contributions_transaction_id",
                table: "goal_contributions"
            );

            migrationBuilder.AddColumn<uint>(
                name: "version",
                table: "goals",
                type: "int unsigned",
                nullable: false,
                defaultValue: 0u
            );

            migrationBuilder.CreateIndex(
                name: "ix_goal_contributions_transaction_id",
                table: "goal_contributions",
                column: "transaction_id"
            );

            migrationBuilder.AddForeignKey(
                name: "fk_goal_contributions_transactions_transaction_id",
                table: "goal_contributions",
                column: "transaction_id",
                principalTable: "transactions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_goal_contributions_transactions_transaction_id",
                table: "goal_contributions"
            );

            migrationBuilder.DropIndex(
                name: "ix_goal_contributions_transaction_id",
                table: "goal_contributions"
            );

            migrationBuilder.DropColumn(name: "version", table: "goals");

            migrationBuilder.CreateIndex(
                name: "ix_goal_contributions_transaction_id",
                table: "goal_contributions",
                column: "transaction_id",
                unique: true
            );

            migrationBuilder.AddForeignKey(
                name: "fk_goal_contributions_transactions_transaction_id",
                table: "goal_contributions",
                column: "transaction_id",
                principalTable: "transactions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict
            );
        }
    }
}
