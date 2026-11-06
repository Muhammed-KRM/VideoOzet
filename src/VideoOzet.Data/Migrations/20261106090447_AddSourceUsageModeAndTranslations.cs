using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoOzet.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceUsageModeAndTranslations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "kullanim_turu",
                table: "videolar",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "cevrilmis_ozet_metni",
                table: "video_summaries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "orijinal_dil",
                table: "video_summaries",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "tr");

            migrationBuilder.AddColumn<int>(
                name: "kullanim_turu",
                table: "dokumanlar",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "cevrilmis_ozet_metni",
                table: "dokuman_metinleri",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "damitilmis_ozet_metni",
                table: "dokuman_metinleri",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "orijinal_dil",
                table: "dokuman_metinleri",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "tr");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "kullanim_turu",
                table: "videolar");

            migrationBuilder.DropColumn(
                name: "cevrilmis_ozet_metni",
                table: "video_summaries");

            migrationBuilder.DropColumn(
                name: "orijinal_dil",
                table: "video_summaries");

            migrationBuilder.DropColumn(
                name: "kullanim_turu",
                table: "dokumanlar");

            migrationBuilder.DropColumn(
                name: "cevrilmis_ozet_metni",
                table: "dokuman_metinleri");

            migrationBuilder.DropColumn(
                name: "damitilmis_ozet_metni",
                table: "dokuman_metinleri");

            migrationBuilder.DropColumn(
                name: "orijinal_dil",
                table: "dokuman_metinleri");
        }
    }
}
