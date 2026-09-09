using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace VideoOzet.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "egitimler",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ad = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    aciklama = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    olusturma_tarihi = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "now()"),
                    guncelleme_tarihi = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    toplam_video_sayisi = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    islenmi_video_sayisi = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    durum = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_egitimler", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "endpoint_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    trace_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    query = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    request_body = table.Column<string>(type: "text", nullable: true),
                    response_body = table.Column<string>(type: "text", nullable: true),
                    status_code = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_endpoint_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "function_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    error_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    class_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    method_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    file_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    line_number = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    stack_trace = table.Column<string>(type: "text", nullable: true),
                    input_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    input_value = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trace_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Error"),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_function_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    video_id = table.Column<Guid>(type: "uuid", nullable: true),
                    egitim_id = table.Column<Guid>(type: "uuid", nullable: true),
                    content_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    asama = table.Column<int>(type: "integer", nullable: false),
                    durum = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    baslangic_zamani = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "now()"),
                    bitis_zamani = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    sure_ms = table.Column<int>(type: "integer", nullable: true),
                    hata_mesaji = table.Column<string>(type: "text", nullable: true),
                    hata_detayi = table.Column<string>(type: "text", nullable: true),
                    girdi_metadata = table.Column<string>(type: "jsonb", nullable: true),
                    cikti_metadata = table.Column<string>(type: "jsonb", nullable: true),
                    trace_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    full_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    refresh_token = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    refresh_token_expiry_time = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "video_chunk_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    video_id = table.Column<Guid>(type: "uuid", nullable: false),
                    egitim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_time_ms = table.Column<int>(type: "integer", nullable: false),
                    end_time_ms = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    embedding = table.Column<Vector>(type: "vector(1536)", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_video_chunk_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "content_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    egitim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    konu = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    hedef_uzunluk = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    hedef_kitle = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    durum = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    olusturma_tarihi = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "now()"),
                    tamamlanma_tarihi = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_content_requests_egitim",
                        column: x => x.egitim_id,
                        principalTable: "egitimler",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "videolar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    egitim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    baslik = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    dosya_yolu = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ses_yolu = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sure = table.Column<TimeSpan>(type: "interval", nullable: true),
                    sira = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    dosya_boyutu = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    islem_durumu = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    olusturma_tarihi = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "now()"),
                    islem_tamamlanma_tarihi = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_videolar", x => x.id);
                    table.ForeignKey(
                        name: "fk_videolar_egitim",
                        column: x => x.egitim_id,
                        principalTable: "egitimler",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "generated_contents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    content_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    arastirma_ozeti = table.Column<string>(type: "text", nullable: false),
                    video_plani = table.Column<string>(type: "text", nullable: false),
                    kullanilan_kaynaklar = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    llm_model = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    token_kullanimi = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    uretim_suresi_ms = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    olusturma_tarihi = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_generated_contents", x => x.id);
                    table.ForeignKey(
                        name: "fk_generated_contents_request",
                        column: x => x.content_request_id,
                        principalTable: "content_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "qc_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    content_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    toplam_iddia_sayisi = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    desteklenen_sayisi = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    belirsiz_sayisi = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    desteklenmeyen_sayisi = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    detayli_rapor = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    guven_skor_yuzde = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 0m),
                    olusturma_tarihi = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_qc_results", x => x.id);
                    table.ForeignKey(
                        name: "fk_qc_results_request",
                        column: x => x.content_request_id,
                        principalTable: "content_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "video_summaries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    video_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ozet_metni = table.Column<string>(type: "text", nullable: false),
                    konu_basliklari = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    konu_etiketleri = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    llm_model = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    token_kullanimi = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    olusturma_tarihi = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_video_summaries", x => x.id);
                    table.ForeignKey(
                        name: "fk_summaries_video",
                        column: x => x.video_id,
                        principalTable: "videolar",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "video_transcripts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    video_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ham_metin = table.Column<string>(type: "text", nullable: false),
                    zaman_damgalari = table.Column<string>(type: "jsonb", nullable: true),
                    dil = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "tr"),
                    kelime_sayisi = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    stt_model = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    stt_suresi_ms = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    olusturma_tarihi = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_video_transcripts", x => x.id);
                    table.ForeignKey(
                        name: "fk_transcripts_video",
                        column: x => x.video_id,
                        principalTable: "videolar",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_content_requests_durum",
                table: "content_requests",
                column: "durum");

            migrationBuilder.CreateIndex(
                name: "idx_content_requests_egitim_id",
                table: "content_requests",
                column: "egitim_id");

            migrationBuilder.CreateIndex(
                name: "idx_egitimler_durum",
                table: "egitimler",
                column: "durum");

            migrationBuilder.CreateIndex(
                name: "idx_egitimler_olusturma",
                table: "egitimler",
                column: "olusturma_tarihi",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "idx_endpoint_logs_created_at",
                table: "endpoint_logs",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "idx_endpoint_logs_path",
                table: "endpoint_logs",
                column: "path");

            migrationBuilder.CreateIndex(
                name: "idx_endpoint_logs_status_code",
                table: "endpoint_logs",
                column: "status_code");

            migrationBuilder.CreateIndex(
                name: "idx_function_logs_class_method",
                table: "function_logs",
                columns: new[] { "class_name", "method_name" });

            migrationBuilder.CreateIndex(
                name: "idx_function_logs_created_at",
                table: "function_logs",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "idx_function_logs_error_code",
                table: "function_logs",
                column: "error_code");

            migrationBuilder.CreateIndex(
                name: "idx_function_logs_severity",
                table: "function_logs",
                column: "severity");

            migrationBuilder.CreateIndex(
                name: "idx_generated_contents_request_id",
                table: "generated_contents",
                column: "content_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_pipeline_logs_asama",
                table: "pipeline_logs",
                column: "asama");

            migrationBuilder.CreateIndex(
                name: "idx_pipeline_logs_content_request",
                table: "pipeline_logs",
                column: "content_request_id");

            migrationBuilder.CreateIndex(
                name: "idx_pipeline_logs_created_at",
                table: "pipeline_logs",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "idx_pipeline_logs_durum",
                table: "pipeline_logs",
                column: "durum");

            migrationBuilder.CreateIndex(
                name: "idx_pipeline_logs_egitim_id",
                table: "pipeline_logs",
                column: "egitim_id");

            migrationBuilder.CreateIndex(
                name: "idx_pipeline_logs_video_id",
                table: "pipeline_logs",
                column: "video_id");

            migrationBuilder.CreateIndex(
                name: "idx_qc_results_request_id",
                table: "qc_results",
                column: "content_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_video_chunk_documents_egitim_id",
                table: "video_chunk_documents",
                column: "egitim_id");

            migrationBuilder.CreateIndex(
                name: "IX_video_chunk_documents_embedding",
                table: "video_chunk_documents",
                column: "embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "idx_video_summaries_video_id",
                table: "video_summaries",
                column: "video_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_video_transcripts_video_id",
                table: "video_transcripts",
                column: "video_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_videolar_durum",
                table: "videolar",
                column: "islem_durumu");

            migrationBuilder.CreateIndex(
                name: "idx_videolar_egitim_id",
                table: "videolar",
                column: "egitim_id");

            migrationBuilder.CreateIndex(
                name: "idx_videolar_egitim_sira",
                table: "videolar",
                columns: new[] { "egitim_id", "sira" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "endpoint_logs");

            migrationBuilder.DropTable(
                name: "function_logs");

            migrationBuilder.DropTable(
                name: "generated_contents");

            migrationBuilder.DropTable(
                name: "pipeline_logs");

            migrationBuilder.DropTable(
                name: "qc_results");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "video_chunk_documents");

            migrationBuilder.DropTable(
                name: "video_summaries");

            migrationBuilder.DropTable(
                name: "video_transcripts");

            migrationBuilder.DropTable(
                name: "content_requests");

            migrationBuilder.DropTable(
                name: "videolar");

            migrationBuilder.DropTable(
                name: "egitimler");
        }
    }
}
