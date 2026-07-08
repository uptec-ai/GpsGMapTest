#!/usr/bin/env python3
r"""
자체 타일서버(localhost) → 앱 타일 폴더(Maps\tiles) 로 전국 z12 타일을 받아 채운다.

전제: Docker openstreetmap-tile-server 가 http://localhost:8080/tile/{z}/{x}/{y}.png 로 떠 있어야 함.
내 서버이므로 대량 다운로드 제한/차단이 없다. 표준 라이브러리만 사용.

동작:
  - 전국 bbox 의 z12 타일을 XYZ 로 받아 GpsMapTester\Maps\tiles\{z}\{x}\{y}.png 로 저장.
  - 이미 실제 타일(>3KB)이 있으면 건너뜀(이어받기). 합성 placeholder(<3KB)는 덮어씀.

사용:
  python pull_korea_z12.py                 # 기본(localhost:8080, 전국, z12)
  python pull_korea_z12.py --url http://localhost:8080/tile/{z}/{x}/{y}.png
"""
import argparse
import math
import os
import time
import urllib.request
from concurrent.futures import ThreadPoolExecutor

# 앱 타일 폴더 (exe 옆 Maps\tiles 로 빌드 시 복사됨)
OUT = os.path.join(
    os.path.dirname(os.path.abspath(__file__)),
    "..", "..", "GpsMapTester", "Maps", "tiles")

Z = 12  # 기본 배율 (--zoom 으로 변경 가능)
# 남한 전역 bbox (제주·울릉 포함) : (west, south, east, north)
BBOX = (124.5, 33.0, 131.0, 38.7)


def lon2x(lon):
    return int((lon + 180.0) / 360.0 * (1 << Z))


def lat2y(lat):
    r = math.radians(lat)
    return int((1.0 - math.log(math.tan(r) + 1.0 / math.cos(r)) / math.pi) / 2.0 * (1 << Z))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--url", default="http://localhost:8080/tile/{z}/{x}/{y}.png")
    ap.add_argument("--workers", type=int, default=6)
    ap.add_argument("--zoom", type=int, default=12, help="배율(기본 12). 앱 FixedZoom 과 맞출 것")
    a = ap.parse_args()

    global Z
    Z = a.zoom
    w, s, e, n = BBOX
    x0, x1 = lon2x(w), lon2x(e)
    y0, y1 = lat2y(n), lat2y(s)
    tiles = [(x, y) for x in range(x0, x1 + 1) for y in range(y0, y1 + 1)]
    outdir = os.path.abspath(OUT)
    print(f"z{Z} 전국: {len(tiles)} tiles  x[{x0}..{x1}] y[{y0}..{y1}]")
    print(f"-> {outdir}")
    print(f"url: {a.url}")

    ok = skip = fail = 0
    t0 = time.time()

    def fetch(t):
        nonlocal ok, skip, fail
        x, y = t
        d = os.path.join(outdir, str(Z), str(x))
        f = os.path.join(d, f"{y}.png")
        if os.path.exists(f) and os.path.getsize(f) > 3000:  # 이미 실제 타일 → skip
            skip += 1
            return
        url = a.url.format(z=Z, x=x, y=y)
        for attempt in range(4):
            try:
                with urllib.request.urlopen(url, timeout=120) as r:
                    if r.status == 200:
                        data = r.read()
                        os.makedirs(d, exist_ok=True)
                        with open(f, "wb") as fp:
                            fp.write(data)
                        ok += 1
                        return
            except Exception:
                time.sleep(0.5 * (attempt + 1))
        fail += 1

    with ThreadPoolExecutor(max_workers=a.workers) as ex:
        for i, _ in enumerate(ex.map(fetch, tiles), 1):
            if i % 200 == 0:
                rate = i / max(1e-6, time.time() - t0)
                print(f"  {i}/{len(tiles)} ok={ok} skip={skip} fail={fail}  {rate:.0f} t/s")

    print(f"[OK] ok={ok} skip={skip} fail={fail}  in {(time.time()-t0)/60:.1f} min")
    if fail:
        print("[WARN] 실패 타일은 재실행하면 이어받습니다(서버 첫 렌더가 느릴 수 있음).")


if __name__ == "__main__":
    main()
