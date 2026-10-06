using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoOzet.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQcFieldsToBolumRevizyonu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "GuvenSkorYuzde",
                table: "BolumRevizyonlari",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AddColumn<int>(
                name: "BelirsizSayisi",
                table: "BolumRevizyonlari",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DesteklenenSayisi",
                table: "BolumRevizyonlari",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DesteklenmeyenSayisi",
                table: "BolumRevizyonlari",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DetayliRapor",
                table: "BolumRevizyonlari",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "QcDurumu",
                table: "BolumRevizyonlari",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ToplamIddiaSayisi",
                table: "BolumRevizyonlari",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BelirsizSayisi",
                table: "BolumRevizyonlari");

            migrationBuilder.DropColumn(
                name: "DesteklenenSayisi",
                table: "BolumRevizyonlari");

            migrationBuilder.DropColumn(
                name: "DesteklenmeyenSayisi",
                table: "BolumRevizyonlari");

            migrationBuilder.DropColumn(
                name: "DetayliRapor",
                table: "BolumRevizyonlari");

            migrationBuilder.DropColumn(
                name: "QcDurumu",
                table: "BolumRevizyonlari");

            migrationBuilder.DropColumn(
                name: "ToplamIddiaSayisi",
                table: "BolumRevizyonlari");

            migrationBuilder.AlterColumn<decimal>(
                name: "GuvenSkorYuzde",
                table: "BolumRevizyonlari",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,2)",
                oldPrecision: 5,
                oldScale: 2);
        }
    }
}
