using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoOzet.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRemoteVideoImportFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "alim_modu",
                table: "videolar",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "dis_kaynak_id",
                table: "videolar",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kanal_adi",
                table: "videolar",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "kaynak_platform",
                table: "videolar",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "kaynak_url",
                table: "videolar",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "thumbnail_url",
                table: "videolar",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "transkript_kalite_skoru",
                table: "videolar",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "transkript_kaynak_notu",
                table: "videolar",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_videolar_egitim_diskaynak",
                table: "videolar",
                columns: new[] { "egitim_id", "dis_kaynak_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_videolar_egitim_diskaynak",
                table: "videolar");

            migrationBuilder.DropColumn(
                name: "alim_modu",
                table: "videolar");

            migrationBuilder.DropColumn(
                name: "dis_kaynak_id",
                table: "videolar");

            migrationBuilder.DropColumn(
                name: "kanal_adi",
                table: "videolar");

            migrationBuilder.DropColumn(
                name: "kaynak_platform",
                table: "videolar");

            migrationBuilder.DropColumn(
                name: "kaynak_url",
                table: "videolar");

            migrationBuilder.DropColumn(
                name: "thumbnail_url",
                table: "videolar");

            migrationBuilder.DropColumn(
                name: "transkript_kalite_skoru",
                table: "videolar");

            migrationBuilder.DropColumn(
                name: "transkript_kaynak_notu",
                table: "videolar");
        }
    }
}
