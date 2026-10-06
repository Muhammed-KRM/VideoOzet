import psycopg2

conn = psycopg2.connect(host='localhost', port=5433, database='videoozet', user='postgres', password='postgres')
c = conn.cursor()
c.execute("""
    SELECT 
        sb."BolumNo", 
        sb."CalismaBasligi", 
        br."RevizyonNo", 
        br."GuvenSkorYuzde", 
        br."QcDurumu", 
        br."ToplamIddiaSayisi", 
        br."DesteklenenSayisi", 
        br."DesteklenmeyenSayisi",
        br."KullanilanKaynaklar"
    FROM "BolumRevizyonlari" br
    JOIN "SeriBolumler" sb ON br."SeriBolumId" = sb."Id"
    ORDER BY sb."BolumNo", br."RevizyonNo"
""")
for row in c.fetchall():
    print(f"Bolum {row[0]}: {row[1]} | Rev {row[2]} | Guven: {row[3]}% | QC: {row[4]} | Total: {row[5]} (Supported: {row[6]}, Unsupported: {row[7]}) | Kaynaklar: {row[8][:60] if row[8] else 'None'}")
conn.close()
