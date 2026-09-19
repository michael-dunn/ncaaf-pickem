using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NcaafPickEm.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase7_01_PushRetries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PushRetries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotificationLogId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    TtlSeconds = table.Column<int>(type: "int", nullable: false),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    NextAttemptUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushRetries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PushRetries_NotificationLog_NotificationLogId",
                        column: x => x.NotificationLogId,
                        principalTable: "NotificationLog",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PushRetries_PushSubscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "PushSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PushRetries_NextAttemptUtc",
                table: "PushRetries",
                column: "NextAttemptUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PushRetries_NotificationLogId_SubscriptionId",
                table: "PushRetries",
                columns: new[] { "NotificationLogId", "SubscriptionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushRetries_SubscriptionId",
                table: "PushRetries",
                column: "SubscriptionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PushRetries");
        }
    }
}
