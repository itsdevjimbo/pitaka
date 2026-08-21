using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PitakaApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddNameAndChangeIsActiveToStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_active",
                table: "recurring_transactions");

            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "recurring_transactions",
                type: "varchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "recurring_transactions",
                type: "varchar(100)",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transactions_user_id_name",
                table: "recurring_transactions",
                columns: new[] { "user_id", "name" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "ix_recurring_transactions_user_id",
                table: "recurring_transactions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_recurring_transactions_user_id",
                table: "recurring_transactions",
                column: "user_id");

            migrationBuilder.DropIndex(
                name: "ix_recurring_transactions_user_id_name",
                table: "recurring_transactions");

            migrationBuilder.DropColumn(
                name: "name",
                table: "recurring_transactions");

            migrationBuilder.DropColumn(
                name: "status",
                table: "recurring_transactions");

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "recurring_transactions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }
    }
}
