using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DeepDrftData.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "track",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entry_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    track_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    artist = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    album = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    genre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    release_date = table.Column<DateOnly>(type: "date", nullable: true),
                    image_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_track", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_track_is_deleted",
                table: "track",
                column: "is_deleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "track");
        }
    }
}
