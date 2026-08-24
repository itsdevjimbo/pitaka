using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PitakaApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class ChangeUserIdToRequiredAndAddUniqueNameConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {       
            migrationBuilder.DropForeignKey(
                name: "fk_tags_users_user_id",
                table: "tags");

            migrationBuilder.AddForeignKey(
                name: "fk_tags_users_user_id",
                table: "tags",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
                
            migrationBuilder.CreateIndex(
                name: "ix_tags_user_id_name",
                table: "tags",
                columns: new[] { "user_id", "name" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "ix_tags_user_id",
                table: "tags");

            migrationBuilder.AlterColumn<int>(
                name: "user_id",
                table: "tags",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tags_users_user_id",
                table: "tags");

            migrationBuilder.AddForeignKey(
                name: "fk_tags_users_user_id",
                table: "tags",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.CreateIndex(
                name: "ix_tags_user_id",
                table: "tags",
                column: "user_id");

            migrationBuilder.DropIndex(
                name: "ix_tags_user_id_name",
                table: "tags");

            migrationBuilder.AlterColumn<int>(
                name: "user_id",
                table: "tags",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");
        }
    }
}
