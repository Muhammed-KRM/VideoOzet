using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VideoOzet.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddModerationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BanReason",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BannedUntil",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastViolationAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ViolationCount",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ViolationLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListingId = table.Column<Guid>(type: "uuid", nullable: true),
                    ListingTitle = table.Column<string>(type: "text", nullable: false),
                    ViolationType = table.Column<string>(type: "text", nullable: false),
                    DetectedContent = table.Column<string>(type: "text", nullable: false),
                    DetectedBy = table.Column<string>(type: "text", nullable: false),
                    IsManual = table.Column<bool>(type: "boolean", nullable: false),
                    AdminNote = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ViolationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ViolationLogs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ViolationLogs_UserId",
                table: "ViolationLogs",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ViolationLogs");

            migrationBuilder.DropColumn(
                name: "BanReason",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "BannedUntil",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastViolationAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ViolationCount",
                table: "Users");
        }
    }
}
