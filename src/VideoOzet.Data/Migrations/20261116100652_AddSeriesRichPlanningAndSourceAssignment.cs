using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoOzet.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSeriesRichPlanningAndSourceAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OnayTalimati",
                table: "SeriPlanlari",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AtananKaynaklarJson",
                table: "SeriBolumler",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "Kanca",
                table: "SeriBolumler",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SahneIskeletiJson",
                table: "SeriBolumler",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "llm_model",
                table: "dokuman_metinleri",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OnayTalimati",
                table: "SeriPlanlari");

            migrationBuilder.DropColumn(
                name: "AtananKaynaklarJson",
                table: "SeriBolumler");

            migrationBuilder.DropColumn(
                name: "Kanca",
                table: "SeriBolumler");

            migrationBuilder.DropColumn(
                name: "SahneIskeletiJson",
                table: "SeriBolumler");

            migrationBuilder.DropColumn(
                name: "llm_model",
                table: "dokuman_metinleri");
        }
    }
}
