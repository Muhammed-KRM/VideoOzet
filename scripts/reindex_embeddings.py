import psycopg2
import urllib.request
import json
import time

def reindex():
    conn = psycopg2.connect(
        host="localhost",
        port=5433,
        database="videoozet",
        user="postgres",
        password="postgres"
    )
    cursor = conn.cursor()

    cursor.execute("SELECT id, text FROM video_chunk_documents WHERE embedding IS NULL")
    rows = cursor.fetchall()
    total = len(rows)
    print(f"Found {total} chunks requiring embedding computation.")

    if total == 0:
        print("All chunks already have embeddings.")
        return

    batch_size = 20
    ollama_url = "http://localhost:11435/api/embed"

    for i in range(0, total, batch_size):
        batch = rows[i:i + batch_size]
        ids = [r[0] for r in batch]
        texts = [r[1] if r[1] and r[1].strip() else "boş metin" for r in batch]

        req = urllib.request.Request(
            ollama_url,
            data=json.dumps({"model": "bge-m3", "input": texts}).encode("utf-8"),
            headers={"Content-Type": "application/json"}
        )

        with urllib.request.urlopen(req) as resp:
            data = json.loads(resp.read().decode("utf-8"))
            embeddings = data.get("embeddings", [])

        if len(embeddings) != len(ids):
            print(f"Mismatch: got {len(embeddings)} embeddings for {len(ids)} inputs!")
            continue

        for chunk_id, emb in zip(ids, embeddings):
            vec_str = "[" + ",".join(str(f) for f in emb) + "]"
            cursor.execute(
                "UPDATE video_chunk_documents SET embedding = %s::vector WHERE id = %s",
                (vec_str, chunk_id)
            )

        conn.commit()
        print(f"Indexed {min(i + batch_size, total)} / {total} chunks...")

    cursor.close()
    conn.close()
    print("Re-indexing complete!")

if __name__ == "__main__":
    reindex()
