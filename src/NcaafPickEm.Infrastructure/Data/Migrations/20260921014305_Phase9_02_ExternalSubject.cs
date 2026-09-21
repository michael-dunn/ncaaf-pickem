using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NcaafPickEm.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase9_02_ExternalSubject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "GoogleSubject",
                table: "Users",
                newName: "ExternalSubject");

            migrationBuilder.RenameIndex(
                name: "IX_Users_GoogleSubject",
                table: "Users",
                newName: "IX_Users_ExternalSubject");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ExternalSubject",
                table: "Users",
                newName: "GoogleSubject");

            migrationBuilder.RenameIndex(
                name: "IX_Users_ExternalSubject",
                table: "Users",
                newName: "IX_Users_GoogleSubject");
        }
    }
}
