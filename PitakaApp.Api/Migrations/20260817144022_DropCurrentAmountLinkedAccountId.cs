using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PitakaApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class DropCurrentAmountLinkedAccountId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_goals_accounts_linked_account_id",
                table: "goals");

            migrationBuilder.DropIndex(
                name: "ix_goals_linked_account_id",
                table: "goals");

            migrationBuilder.DropColumn(
                name: "current_amount",
                table: "goals");

            migrationBuilder.DropColumn(
                name: "linked_account_id",
                table: "goals");

            migrationBuilder.CreateIndex(
                name: "ix_goals_user_id_name",
                table: "goals",
                columns: new[] { "user_id", "name" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "ix_goals_user_id",
                table: "goals");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_goals_user_id",
                table: "goals",
                column: "user_id");

            migrationBuilder.DropIndex(
                name: "ix_goals_user_id_name",
                table: "goals");

            migrationBuilder.AddColumn<decimal>(
                name: "current_amount",
                table: "goals",
                type: "decimal(14,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "linked_account_id",
                table: "goals",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_goals_linked_account_id",
                table: "goals",
                column: "linked_account_id");

            migrationBuilder.AddForeignKey(
                name: "fk_goals_accounts_linked_account_id",
                table: "goals",
                column: "linked_account_id",
                principalTable: "accounts",
                principalColumn: "id");
        }
    }
}
