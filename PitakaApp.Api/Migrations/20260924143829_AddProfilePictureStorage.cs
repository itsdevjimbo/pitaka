using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PitakaApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProfilePictureStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder
                .AddColumn<string>(
                    name: "profile_picture_media_type",
                    table: "users",
                    type: "varchar(32)",
                    maxLength: 32,
                    nullable: true
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .AddColumn<string>(
                    name: "profile_picture_object_key",
                    table: "users",
                    type: "varchar(128)",
                    maxLength: 128,
                    nullable: true
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "profile_picture_objects",
                    columns: table => new
                    {
                        object_key = table
                            .Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
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
                        table.PrimaryKey("pk_profile_picture_objects", x => x.object_key);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_profile_picture_objects_state_next_attempt_at",
                table: "profile_picture_objects",
                columns: ["state", "next_attempt_at"]
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "profile_picture_objects");

            migrationBuilder.DropColumn(name: "profile_picture_media_type", table: "users");

            migrationBuilder.DropColumn(name: "profile_picture_object_key", table: "users");
        }
    }
}
