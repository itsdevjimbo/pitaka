using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PitakaApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class UseFilesForPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "photo_id",
                table: "users",
                type: "int",
                nullable: true
            );

            migrationBuilder
                .CreateTable(
                    name: "files",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        object_key = table
                            .Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        media_type = table
                            .Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        state = table
                            .Column<string>(type: "varchar(100)", nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                        next_attempt_at = table.Column<DateTime>(
                            type: "datetime(6)",
                            nullable: true
                        ),
                        deletion_lease_until = table.Column<DateTime>(
                            type: "datetime(6)",
                            nullable: true
                        ),
                        deletion_lease_token = table
                            .Column<string>(type: "varchar(36)", maxLength: 36, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        deletion_attempts = table.Column<int>(type: "int", nullable: false),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("pk_files", x => x.id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_users_photo_id",
                table: "users",
                column: "photo_id",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "ix_files_object_key",
                table: "files",
                column: "object_key",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "ix_files_state_next_attempt_at",
                table: "files",
                columns: ["state", "next_attempt_at"]
            );

            migrationBuilder.AddForeignKey(
                name: "fk_users_files_photo_id",
                table: "users",
                column: "photo_id",
                principalTable: "files",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "fk_users_files_photo_id", table: "users");

            migrationBuilder.DropIndex(name: "ix_users_photo_id", table: "users");

            migrationBuilder.DropColumn(name: "photo_id", table: "users");

            migrationBuilder.DropTable(name: "files");
        }
    }
}
