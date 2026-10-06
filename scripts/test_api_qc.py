import urllib.request
import json

for ep_no in [1, 2]:
    url = f"http://localhost:5002/api/series-requests/82682be6-40d2-44e9-a67e-337e119acdb0/plans/1/videos/{ep_no}"
    req = urllib.request.Request(url, headers={'X-Api-Key': 'SUPER_SECRET_API_KEY_123!'})
    with urllib.request.urlopen(req) as resp:
        data = json.loads(resp.read().decode('utf-8'))
        print(f"\nAPI RESPONSE FOR BÖLÜM {ep_no}:")
        print("  Başlık:", data.get('calismaBasligi'))
        print("  Aktif Revizyon ID:", data.get('aktifRevizyonId'))
        revs = data.get('revizyonlar', [])
        print(f"  Toplam Revizyon: {len(revs)}")
        for r in revs:
            print(f"    -> Rev {r.get('revizyonNo')}: Güven = %{r.get('guvenSkorYuzde')}, QC Durumu = {r.get('qcDurumu')}, İddialar = {r.get('toplamIddiaSayisi')} (Desteklenen: {r.get('desteklenenSayisi')}, Desteklenmeyen: {r.get('desteklenmeyenSayisi')})")
