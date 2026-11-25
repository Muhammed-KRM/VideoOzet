using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VideoOzet.Data.Context;
using VideoOzet.Data.Enums;
using Xunit;

namespace VideoOzet.UnitTests.API;

public class SqliteSchemaUpgradeTests
{
    [Fact]
    public void EnsureSqliteVideolarSchemaUpgraded_ShouldAddAll8ColumnsAndIndexIdempotently()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"videoozet_schema_upgrade_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            var existingId = Guid.NewGuid();
            var existingEgitimId = Guid.NewGuid();

            using (var db = new AppDbContext(options))
            {
                db.Database.OpenConnection();
                db.Database.ExecuteSqlRaw("""
                    CREATE TABLE videolar (
                        id TEXT NOT NULL PRIMARY KEY,
                        egitim_id TEXT NOT NULL,
                        baslik TEXT NOT NULL,
                        dosya_yolu TEXT NOT NULL,
                        ses_yolu TEXT NULL,
                        sure TEXT NULL,
                        sira INTEGER NOT NULL DEFAULT 0,
                        dosya_boyutu INTEGER NOT NULL DEFAULT 0,
                        islem_durumu INTEGER NOT NULL DEFAULT 0,
                        kullanim_turu INTEGER NOT NULL DEFAULT 0,
                        IndekslemeTuru INTEGER NOT NULL DEFAULT 0,
                        olusturma_tarihi TEXT NOT NULL,
                        islem_tamamlanma_tarihi TEXT NULL
                    );
                """);

                db.Database.ExecuteSqlRaw(
                    """
                    INSERT INTO videolar (id, egitim_id, baslik, dosya_yolu, sira, dosya_boyutu, islem_durumu, kullanim_turu, IndekslemeTuru, olusturma_tarihi)
                    VALUES ({0}, {1}, 'Legacy Video', 'videos/legacy.mp4', 1, 1024, 9, 0, 0, '2026-01-01T00:00:00Z');
                    """,
                    existingId.ToString().ToUpperInvariant(),
                    existingEgitimId.ToString().ToUpperInvariant());

                // Run twice to verify full idempotency
                global::Program.EnsureSqliteVideolarSchemaUpgraded(db);
                global::Program.EnsureSqliteVideolarSchemaUpgraded(db);

                var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var conn = db.Database.GetDbConnection();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA table_info(videolar);";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        columns.Add(reader.GetString(reader.GetOrdinal("name")));
                    }
                }

                columns.Should().Contain(new[]
                {
                    "kaynak_platform",
                    "kaynak_url",
                    "dis_kaynak_id",
                    "kanal_adi",
                    "thumbnail_url",
                    "alim_modu",
                    "transkript_kalite_skoru",
                    "transkript_kaynak_notu"
                });

                var indexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var idxCmd = conn.CreateCommand())
                {
                    idxCmd.CommandText = "PRAGMA index_list(videolar);";
                    using var idxReader = idxCmd.ExecuteReader();
                    while (idxReader.Read())
                    {
                        indexes.Add(idxReader.GetString(idxReader.GetOrdinal("name")));
                    }
                }

                indexes.Should().Contain("idx_videolar_egitim_diskaynak");

                var loaded = db.Videolar.AsNoTracking().Single(v => v.Id == existingId);
                loaded.Baslik.Should().Be("Legacy Video");
                loaded.KaynakPlatform.Should().Be(KaynakPlatform.Yerel);
                loaded.AlimModu.Should().Be(UzaktanAlimModu.AkilliTranskriptOncelikli);
                loaded.TranskriptKaliteSkoru.Should().BeNull();
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                try { File.Delete(dbPath); } catch { }
            }
        }
    }
}
