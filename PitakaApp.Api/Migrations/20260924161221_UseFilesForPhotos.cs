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

            migrationBuilder.Sql(
                """
                INSERT INTO files (
                    object_key,
                    media_type,
                    state,
                    created_at,
                    next_attempt_at,
                    deletion_lease_until,
                    deletion_lease_token,
                    deletion_attempts
                )
                SELECT
                    p.object_key,
                    COALESCE(u.profile_picture_media_type, 'application/octet-stream'),
                    CASE p.state WHEN 'Current' THEN 'Available' ELSE p.state END,
                    p.created_at,
                    p.next_attempt_at,
                    p.deletion_lease_until,
                    p.deletion_lease_token,
                    p.deletion_attempts
                FROM profile_picture_objects AS p
                LEFT JOIN users AS u
                    ON u.profile_picture_object_key = p.object_key;
                """
            );

            migrationBuilder.Sql(
                """
                UPDATE users AS u
                INNER JOIN files AS f
                    ON f.object_key = u.profile_picture_object_key
                SET u.photo_id = f.id;
                """
            );

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
                onDelete: ReferentialAction.Restrict
            );

            migrationBuilder.DropTable(name: "profile_picture_objects");

            migrationBuilder.DropColumn(name: "profile_picture_media_type", table: "users");

            migrationBuilder.DropColumn(name: "profile_picture_object_key", table: "users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
                        created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                        deletion_attempts = table.Column<int>(type: "int", nullable: false),
                        deletion_lease_token = table
                            .Column<string>(type: "varchar(36)", maxLength: 36, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        deletion_lease_until = table.Column<DateTime>(
                            type: "datetime(6)",
                            nullable: true
                        ),
                        next_attempt_at = table.Column<DateTime>(
                            type: "datetime(6)",
                            nullable: true
                        ),
                        state = table
                            .Column<string>(type: "varchar(100)", nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("pk_profile_picture_objects", x => x.object_key);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql(
                """
                INSERT INTO profile_picture_objects (
                    object_key,
                    state,
                    created_at,
                    next_attempt_at,
                    deletion_lease_until,
                    deletion_lease_token,
                    deletion_attempts
                )
                SELECT
                    object_key,
                    CASE state WHEN 'Available' THEN 'Current' ELSE state END,
                    created_at,
                    next_attempt_at,
                    deletion_lease_until,
                    deletion_lease_token,
                    deletion_attempts
                FROM files;
                """
            );

            migrationBuilder.Sql(
                """
                UPDATE users AS u
                INNER JOIN files AS f ON f.id = u.photo_id
                SET
                    u.profile_picture_object_key = f.object_key,
                    u.profile_picture_media_type = f.media_type;
                """
            );

            migrationBuilder.CreateIndex(
                name: "ix_profile_picture_objects_state_next_attempt_at",
                table: "profile_picture_objects",
                columns: ["state", "next_attempt_at"]
            );

            migrationBuilder.DropForeignKey(name: "fk_users_files_photo_id", table: "users");

            migrationBuilder.DropIndex(name: "ix_users_photo_id", table: "users");

            migrationBuilder.DropColumn(name: "photo_id", table: "users");

            migrationBuilder.DropTable(name: "files");
        }
    }
}
