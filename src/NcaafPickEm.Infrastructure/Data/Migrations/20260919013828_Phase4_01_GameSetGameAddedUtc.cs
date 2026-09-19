using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NcaafPickEm.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase4_01_GameSetGameAddedUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AddedUtc",
                table: "WeekGameSetGames",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddedUtc",
                table: "WeekGameSetGames");
        }
    }
}
