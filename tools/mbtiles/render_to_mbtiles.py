#!/usr/bin/env python3
r"""
자체 타일 서버(localhost) → MBTiles 다운로더/패키저.

Docker openstreetmap-tile-server 등 '내 서버'에서 z15 타일을 받아 MBTiles로 저장합니다.
내 서버이므로 대량 다운로드 정책/차단 문제가 없습니다. 표준 라이브러리만 사용합니다.

전제: 로컬 타일 서버가 떠 있어야 함 (예: http://localhost:8080/tile/{z}/{x}/{y}.png)

사용:
  # 타일 수/용량만 미리 확인 (서버 불필요)
  python render_to_mbtiles.py --out korea_z15.mbtiles --dry-run

  # 실제 다운로드 (4개 지역, z15)
  python render_to_mbtiles.py --out korea_z15.mbtiles

  # 일부 지역만 / URL·배율 지정
  python render_to_mbtiles.py --regions seoul daejeon --zoom 15 \
      --url "http://localhost:8080/tile/{z}/{x}/{y}.png" --out out.mbtiles

만든 .mbtiles 를 <exe폴더>\Maps\tiles.mbtiles 로 복사하면 앱이 오프라인으로 표출합니다.
재실행하면 이미 받은 타일은 건너뜁니다(이어받기).
"""
import argparse
import math
import os
import sqlite3
import sys
import threading
import time
import urllib.request
from concurrent.futures import ThreadPoolExecutor

# key: (west, south, east, north)
REGIONS = {
    "seoul":    (126.734, 37.413, 127.269, 37.715),
    "gyeonggi": (126.50,  36.90,  127.90,  38.30),
    "naepo":    (126.58,  36.58,  126.75,  36.73),
    "daejeon":  (127.25,  36.18,  127.56,  36.50),
}


def lon2x(lon, z):
    return int((lon + 180.0) / 360.0 * (1 << z))


def lat2y(lat, z):
    r = math.radians(lat)
    return int((1.0 - math.log(math.tan(r) + 1.0 / math.cos(r)) / math.pi) / 2.0 * (1 << z))


def tiles_for(bbox, z):
    w, s, e, n = bbox
    x0, x1 = lon2x(w, z), lon2x(e, z)
    y0, y1 = lat2y(n, z), lat2y(s, z)  # 북쪽이 작은 y
    for x in range(min(x0, x1), max(x0, x1) + 1):
        for y in range(min(y0, y1), max(y0, y1) + 1):
            yield x, y


def main():
    ap = argparse.ArgumentParser(description="localhost 타일서버 → MBTiles")
    ap.add_argument("--url", default="http://localhost:8080/tile/{z}/{x}/{y}.png")
    ap.add_argument("--zoom", type=int, default=15)
    ap.add_argument("--regions", nargs="*", default=list(REGIONS.keys()))
    ap.add_argument("--out", required=True)
    ap.add_argument("--workers", type=int, default=6)
    ap.add_argument("--dry-run", action="store_true", help="다운로드 없이 타일 수만 계산")
    a = ap.parse_args()
    z = a.zoom

    want = set()
    per_region = {}
    for key in a.regions:
        if key not in REGIONS:
            sys.exit(f"[ERR] unknown region: {key} (choices: {', '.join(REGIONS)})")
        cnt = 0
        for x, y in tiles_for(REGIONS[key], z):
            want.add((x, y))
            cnt += 1
        per_region[key] = cnt

    total = len(want)
    for key in a.regions:
        print(f"  {key:10s}: {per_region[key]:>7,} tiles (bbox)")
    print(f"  {'UNIQUE':10s}: {total:>7,} tiles @ z{z}  (~{total*20/1024:.0f} MB @20KB/tile)")

    if a.dry_run:
        print("[dry-run] 다운로드 생략")
        return

    con = sqlite3.connect(a.out, check_same_thread=False)
    cur = con.cursor()
    cur.execute("CREATE TABLE IF NOT EXISTS metadata(name text, value text)")
    cur.execute("CREATE TABLE IF NOT EXISTS tiles(zoom_level integer, tile_column integer, tile_row integer, tile_data blob)")
    cur.execute("CREATE UNIQUE INDEX IF NOT EXISTS tile_index on tiles(zoom_level, tile_column, tile_row)")
    con.commit()

    have = set()
    for x, tms in cur.execute("SELECT tile_column, tile_row FROM tiles WHERE zoom_level=?", (z,)):
        have.add((x, (1 << z) - 1 - tms))
    todo = [t for t in want if t not in have]
    print(f"already have {len(have & want)}, to download {len(todo)}  (url={a.url})")

    lock = threading.Lock()
    c = {"ok": 0, "fail": 0, "done": 0}
    headers = {"User-Agent": "GpsMapTester-LocalPrefetch/1.0"}

    def fetch(t):
        x, y = t
        url = a.url.format(z=z, x=x, y=y)
        for attempt in range(4):
            try:
                req = urllib.request.Request(url, headers=headers)
                with urllib.request.urlopen(req, timeout=60) as r:
                    if r.status == 200:
                        return t, r.read()
            except Exception:
                time.sleep(0.4 * (attempt + 1))
        return t, None

    t0 = time.time()
    with ThreadPoolExecutor(max_workers=a.workers) as ex:
        for (x, y), data in ex.map(fetch, todo):
            with lock:
                c["done"] += 1
                if data:
                    tms = (1 << z) - 1 - y
                    cur.execute("INSERT OR REPLACE INTO tiles VALUES(?,?,?,?)", (z, x, tms, sqlite3.Binary(data)))
                    c["ok"] += 1
                    if c["ok"] % 200 == 0:
                        con.commit()
                else:
                    c["fail"] += 1
                if c["done"] % 200 == 0 or c["done"] == len(todo):
                    rate = c["done"] / max(1e-6, time.time() - t0)
                    print(f"  {c['done']}/{len(todo)} ok={c['ok']} fail={c['fail']} {rate:.0f} t/s")

    cur.execute("DELETE FROM metadata")
    for k, v in {"name": "korea tiles", "format": "png", "type": "baselayer",
                 "minzoom": str(z), "maxzoom": str(z)}.items():
        cur.execute("INSERT INTO metadata VALUES(?,?)", (k, v))
    con.commit()
    con.close()
    print(f"[OK] {a.out}  ok={c['ok']} fail={c['fail']}  in {(time.time()-t0)/60:.1f} min")
    if c["fail"]:
        print("[WARN] 실패 타일이 있습니다. 서버 렌더가 느리면(첫 요청) 재실행하면 이어받습니다.")


if __name__ == "__main__":
    main()
