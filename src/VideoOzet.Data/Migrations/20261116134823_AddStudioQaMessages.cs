using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoOzet.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStudioQaMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "studio_qa_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    egitim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    bolum_no = table.Column<int>(type: "integer", nullable: true),
                    kaynak_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kapsam = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    rol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    mesaj = table.Column<string>(type: "text", nullable: false),
                    oneri_json = table.Column<string>(type: "text", nullable: true),
                    kullanilan_baglam_ozeti = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    olusturma_tarihi = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_studio_qa_messages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_studio_qa_egitim_kapsam",
                table: "studio_qa_messages",
                columns: new[] { "egitim_id", "kapsam", "olusturma_tarihi" });

            migrationBuilder.CreateIndex(
                name: "idx_studio_qa_kaynak",
                table: "studio_qa_messages",
                columns: new[] { "kaynak_id", "olusturma_tarihi" });

            migrationBuilder.CreateIndex(
                name: "idx_studio_qa_request_bolum",
                table: "studio_qa_messages",
                columns: new[] { "content_request_id", "bolum_no", "olusturma_tarihi" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "studio_qa_messages");
        }
    }
}
