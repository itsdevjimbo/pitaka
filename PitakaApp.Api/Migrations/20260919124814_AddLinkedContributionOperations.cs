using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PitakaApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkedContributionOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder
                .CreateTable(
                    name: "linked_contribution_operations",
                    columns: table => new
                    {
                        id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySql:ValueGenerationStrategy",
                                MySqlValueGenerationStrategy.IdentityColumn
                            ),
                        user_id = table.Column<int>(type: "int", nullable: false),
                        key = table
                            .Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        fingerprint = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        status_code = table.Column<int>(type: "int", nullable: true),
                        response_body = table
                            .Column<string>(type: "longtext", nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                        updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("pk_linked_contribution_operations", x => x.id);
                        table.ForeignKey(
                            name: "fk_linked_contribution_operations_users_user_id",
                            column: x => x.user_id,
                            principalTable: "users",
                            principalColumn: "id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_linked_contribution_operations_user_id_key",
                table: "linked_contribution_operations",
                columns: ["user_id", "key"],
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "linked_contribution_operations");
        }
    }
}
