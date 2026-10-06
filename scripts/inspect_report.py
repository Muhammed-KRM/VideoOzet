import psycopg2
import json
import sys

sys.stdout.reconfigure(encoding='utf-8')

conn = psycopg2.connect(host='localhost', port=5433, database='videoozet', user='postgres', password='postgres')
c = conn.cursor()
c.execute("""
    SELECT 
        sb."BolumNo", 
        sb."CalismaBasligi", 
        br."RevizyonNo", 
        br."GuvenSkorYuzde", 
        br."DetayliRapor"
    FROM "BolumRevizyonlari" br
    JOIN "SeriBolumler" sb ON br."SeriBolumId" = sb."Id"
    WHERE br."RevizyonNo" = 2
    ORDER BY sb."BolumNo"
""")
rows = c.fetchall()
for r in rows:
    print(f"\n==========================================")
    print(f"BÖLÜM {r[0]}: {r[1]} (Rev {r[2]}) - GÜVEN: %{r[3]}")
    print(f"==========================================")
    if r[4]:
        try:
            report = json.loads(r[4])
            for i, item in enumerate(report, 1):
                status_icon = "✅" if item.get('durum') == 'destekleniyor' else ("❌" if item.get('durum') == 'desteklenmiyor' else "⚠️")
                print(f"{i}. {status_icon} [{item.get('durum', '').upper()}] {item.get('iddia')}")
                if item.get('kaynakReferansi'):
                    print(f"   Referans: {item.get('kaynakReferansi')}")
                if item.get('aciklama'):
                    print(f"   Açıklama: {item.get('aciklama')}")
        except Exception as e:
            print("JSON parse error:", e)
conn.close()
