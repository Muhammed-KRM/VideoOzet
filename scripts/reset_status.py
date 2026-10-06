import psycopg2

conn = psycopg2.connect(host='localhost', port=5433, database='videoozet', user='postgres', password='postgres')
c = conn.cursor()
c.execute('SELECT "Id", "BolumNo", "CalismaBasligi", "Durum" FROM "SeriBolumler" ORDER BY "BolumNo"')
rows = c.fetchall()
print("Current Episodes:")
for r in rows:
    print(f"  Bolum {r[1]}: {r[2]} | Durum={r[3]}")

c.execute('UPDATE "SeriBolumler" SET "Durum" = 0 WHERE "Durum" = 1')
conn.commit()
print("Reset in-progress statuses to 0 (Beklemede).")
conn.close()
