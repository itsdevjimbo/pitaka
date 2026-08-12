using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PitakaApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueCategoryNamePerUserIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create the new composite index first — MySQL requires an index covering
            // user_id to exist continuously to satisfy the FK constraint, so the old
            // single-column index can't be dropped until this one (which also starts
            // with user_id) is already in place to take over that job.
            migrationBuilder.CreateIndex(
                name: "ix_categories_user_id_name",
                table: "categories",
                columns: new[] { "user_id", "name" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "ix_categories_user_id",
                table: "categories");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_categories_user_id",
                table: "categories",
                column: "user_id");

            migrationBuilder.DropIndex(
                name: "ix_categories_user_id_name",
                table: "categories");
        }
    }
}
