using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PitakaApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIsActiveToCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValue: true backfills every existing category to active. ADR 0004:
            // "Categories that are already in use are unaffected" — nothing that exists
            // today should land retired.
            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "categories",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_active",
                table: "categories");
        }
    }
}
