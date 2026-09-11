using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoOzet.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDokumanEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dokumanlar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    egitim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dosya_adi = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    dosya_yolu = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    uzanti = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    dosya_boyutu = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    islem_durumu = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    olusturma_tarihi = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "now()"),
                    islem_tamamlanma_tarihi = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dokumanlar", x => x.id);
                    table.ForeignKey(
                        name: "fk_dokumanlar_egitim",
                        column: x => x.egitim_id,
                        principalTable: "egitimler",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dokuman_metinleri",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    dokuman_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ham_metin = table.Column<string>(type: "text", nullable: false),
                    kelime_sayisi = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    olusturma_tarihi = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dokuman_metinleri", x => x.id);
                    table.ForeignKey(
                        name: "fk_dokuman_metinleri_dokuman",
                        column: x => x.dokuman_id,
                        principalTable: "dokumanlar",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_dokuman_metinleri_dokuman_id",
                table: "dokuman_metinleri",
                column: "dokuman_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_dokumanlar_durum",
                table: "dokumanlar",
                column: "islem_durumu");

            migrationBuilder.CreateIndex(
                name: "idx_dokumanlar_egitim_id",
                table: "dokumanlar",
                column: "egitim_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dokuman_metinleri");

            migrationBuilder.DropTable(
                name: "dokumanlar");
        }
    }
}
