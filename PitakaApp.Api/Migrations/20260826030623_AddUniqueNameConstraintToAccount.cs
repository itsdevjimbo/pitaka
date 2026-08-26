using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PitakaApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueNameConstraintToAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.CreateIndex(
                name: "ix_accounts_user_id_name",
                table: "accounts",
                columns: new[] { "user_id", "name" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "ix_accounts_user_id",
                table: "accounts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_accounts_user_id",
                table: "accounts",
                column: "user_id");

            migrationBuilder.DropIndex(
                name: "ix_accounts_user_id_name",
                table: "accounts");
        }
    }
}
