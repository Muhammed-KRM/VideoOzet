using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoOzet.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSeriesPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "konu",
                table: "content_requests",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AddColumn<string>(
                name: "EkTonTalimati",
                table: "content_requests",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Mod",
                table: "content_requests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "content_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    content_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    versiyon_no = table.Column<int>(type: "integer", nullable: false),
                    arastirma_ozeti = table.Column<string>(type: "text", nullable: false),
                    video_plani = table.Column<string>(type: "text", nullable: false),
                    revize_talimati = table.Column<string>(type: "text", nullable: true),
                    llm_model = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    olusturma_tarihi = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "now()"),
                    toplam_iddia_sayisi = table.Column<int>(type: "integer", nullable: true),
                    desteklenen_sayisi = table.Column<int>(type: "integer", nullable: true),
                    belirsiz_sayisi = table.Column<int>(type: "integer", nullable: true),
                    desteklenmeyen_sayisi = table.Column<int>(type: "integer", nullable: true),
                    detayli_rapor = table.Column<string>(type: "text", nullable: true, defaultValue: "[]"),
                    guven_skor_yuzde = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    qc_tarihi = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_content_versions_request",
                        column: x => x.content_request_id,
                        principalTable: "content_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KaynakKonuCikarimlari",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KaynakId = table.Column<Guid>(type: "uuid", nullable: false),
                    KaynakTuru = table.Column<string>(type: "text", nullable: false),
                    KonularJson = table.Column<string>(type: "text", nullable: false),
                    PromptVersiyonu = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KaynakKonuCikarimlari", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KonuAnalizleri",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnaFikir = table.Column<string>(type: "text", nullable: false),
                    BeklenenKonularJson = table.Column<string>(type: "text", nullable: false),
                    KonuHaritasiJson = table.Column<string>(type: "text", nullable: false),
                    KaynaktaOlmayanlarJson = table.Column<string>(type: "text", nullable: false),
                    OnerilenVideoSayisi = table.Column<int>(type: "integer", nullable: false),
                    OneriSuresiDk = table.Column<int>(type: "integer", nullable: false),
                    OneriGerekcesi = table.Column<string>(type: "text", nullable: false),
                    Durum = table.Column<int>(type: "integer", nullable: false),
                    LlmModel = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KonuAnalizleri", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KonuAnalizleri_content_requests_ContentRequestId",
                        column: x => x.ContentRequestId,
                        principalTable: "content_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeriPlanlari",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanNo = table.Column<int>(type: "integer", nullable: false),
                    VideoSayisi = table.Column<int>(type: "integer", nullable: false),
                    VarsayilanVideoSuresiDk = table.Column<int>(type: "integer", nullable: false),
                    SeriHaritasiJson = table.Column<string>(type: "text", nullable: false),
                    DisaridaBirakilanlarJson = table.Column<string>(type: "text", nullable: false),
                    KullaniciKisitlariJson = table.Column<string>(type: "text", nullable: false),
                    OneridenFarkli = table.Column<bool>(type: "boolean", nullable: false),
                    Onaylandi = table.Column<bool>(type: "boolean", nullable: false),
                    Durum = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriPlanlari", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeriPlanlari_content_requests_ContentRequestId",
                        column: x => x.ContentRequestId,
                        principalTable: "content_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeriBolumler",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriPlaniId = table.Column<Guid>(type: "uuid", nullable: false),
                    BolumNo = table.Column<int>(type: "integer", nullable: false),
                    CalismaBasligi = table.Column<string>(type: "text", nullable: false),
                    AnaFikir = table.Column<string>(type: "text", nullable: false),
                    HedefSureDk = table.Column<int>(type: "integer", nullable: false),
                    KonularJson = table.Column<string>(type: "text", nullable: false),
                    AktifRevizyonId = table.Column<Guid>(type: "uuid", nullable: true),
                    BaglamEskidi = table.Column<bool>(type: "boolean", nullable: false),
                    BaglamSorunlariJson = table.Column<string>(type: "text", nullable: false),
                    Durum = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriBolumler", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeriBolumler_SeriPlanlari_SeriPlaniId",
                        column: x => x.SeriPlaniId,
                        principalTable: "SeriPlanlari",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BolumRevizyonlari",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriBolumId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevizyonNo = table.Column<int>(type: "integer", nullable: false),
                    Tip = table.Column<int>(type: "integer", nullable: false),
                    Talimat = table.Column<string>(type: "text", nullable: false),
                    HedefAlan = table.Column<string>(type: "text", nullable: false),
                    ArastirmaOzeti = table.Column<string>(type: "text", nullable: false),
                    VideoPlani = table.Column<string>(type: "text", nullable: false),
                    DevirNotuJson = table.Column<string>(type: "text", nullable: false),
                    BaglamJson = table.Column<string>(type: "text", nullable: false),
                    KullanilanKaynaklar = table.Column<string>(type: "text", nullable: false),
                    GuvenSkorYuzde = table.Column<decimal>(type: "numeric", nullable: false),
                    LlmModel = table.Column<string>(type: "text", nullable: false),
                    Durum = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BolumRevizyonlari", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BolumRevizyonlari_SeriBolumler_SeriBolumId",
                        column: x => x.SeriBolumId,
                        principalTable: "SeriBolumler",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BolumRevizyonlari_SeriBolumId_RevizyonNo",
                table: "BolumRevizyonlari",
                columns: new[] { "SeriBolumId", "RevizyonNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_content_versions_req_ver",
                table: "content_versions",
                columns: new[] { "content_request_id", "versiyon_no" });

            migrationBuilder.CreateIndex(
                name: "IX_KaynakKonuCikarimlari_KaynakId_KaynakTuru",
                table: "KaynakKonuCikarimlari",
                columns: new[] { "KaynakId", "KaynakTuru" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KonuAnalizleri_ContentRequestId",
                table: "KonuAnalizleri",
                column: "ContentRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeriBolumler_SeriPlaniId_BolumNo",
                table: "SeriBolumler",
                columns: new[] { "SeriPlaniId", "BolumNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeriPlanlari_ContentRequestId_PlanNo",
                table: "SeriPlanlari",
                columns: new[] { "ContentRequestId", "PlanNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BolumRevizyonlari");

            migrationBuilder.DropTable(
                name: "content_versions");

            migrationBuilder.DropTable(
                name: "KaynakKonuCikarimlari");

            migrationBuilder.DropTable(
                name: "KonuAnalizleri");

            migrationBuilder.DropTable(
                name: "SeriBolumler");

            migrationBuilder.DropTable(
                name: "SeriPlanlari");

            migrationBuilder.DropColumn(
                name: "EkTonTalimati",
                table: "content_requests");

            migrationBuilder.DropColumn(
                name: "Mod",
                table: "content_requests");

            migrationBuilder.AlterColumn<string>(
                name: "konu",
                table: "content_requests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");
        }
    }
}
