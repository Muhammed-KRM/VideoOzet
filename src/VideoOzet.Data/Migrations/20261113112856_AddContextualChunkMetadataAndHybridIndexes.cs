using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoOzet.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContextualChunkMetadataAndHybridIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:vector", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.AddColumn<string>(
                name: "bolum_basligi",
                table: "video_chunk_documents",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "chunk_index",
                table: "video_chunk_documents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "chunk_tipi",
                table: "video_chunk_documents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "kaynak_baslik",
                table: "video_chunk_documents",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "kaynak_dil",
                table: "video_chunk_documents",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "tr");

            migrationBuilder.AddColumn<int>(
                name: "kullanim_turu",
                table: "video_chunk_documents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_video_chunk_documents_egitim_id_chunk_tipi",
                table: "video_chunk_documents",
                columns: new[] { "egitim_id", "chunk_tipi" });

            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
                migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS ix_video_chunk_documents_fts ON video_chunk_documents USING gin (to_tsvector('simple', text));");
                migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS ix_video_chunk_documents_trgm ON video_chunk_documents USING gin (text gin_trgm_ops);");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql("DROP INDEX IF EXISTS ix_video_chunk_documents_trgm;");
                migrationBuilder.Sql("DROP INDEX IF EXISTS ix_video_chunk_documents_fts;");
            }

            migrationBuilder.DropIndex(
                name: "IX_video_chunk_documents_egitim_id_chunk_tipi",
                table: "video_chunk_documents");

            migrationBuilder.DropColumn(
                name: "bolum_basligi",
                table: "video_chunk_documents");

            migrationBuilder.DropColumn(
                name: "chunk_index",
                table: "video_chunk_documents");

            migrationBuilder.DropColumn(
                name: "chunk_tipi",
                table: "video_chunk_documents");

            migrationBuilder.DropColumn(
                name: "kaynak_baslik",
                table: "video_chunk_documents");

            migrationBuilder.DropColumn(
                name: "kaynak_dil",
                table: "video_chunk_documents");

            migrationBuilder.DropColumn(
                name: "kullanim_turu",
                table: "video_chunk_documents");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
        }
    }
}
