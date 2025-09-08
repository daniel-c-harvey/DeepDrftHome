using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeepDrftWeb.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "track",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    entry_key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    track_name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    artist = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    album = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    genre = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    release_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    image_path = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_track", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "track");
        }
    }
}
