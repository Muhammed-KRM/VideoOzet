using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoOzet.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "Notifications",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "Notifications",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "Notifications");
        }
    }
}
