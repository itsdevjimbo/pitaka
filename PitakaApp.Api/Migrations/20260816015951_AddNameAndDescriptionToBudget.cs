using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PitakaApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddNameAndDescriptionToBudget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "budgets",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "budgets",
                type: "varchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_budgets_user_id_name",
                table: "budgets",
                columns: new[] { "user_id", "name" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "ix_budgets_user_id",
                table: "budgets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_budgets_user_id",
                table: "budgets",
                column: "user_id");

            migrationBuilder.DropIndex(
                name: "ix_budgets_user_id_name",
                table: "budgets");

            migrationBuilder.DropColumn(
                name: "description",
                table: "budgets");

            migrationBuilder.DropColumn(
                name: "name",
                table: "budgets");
        }
    }
}
