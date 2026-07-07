#!/usr/bin/env python3
r"""
z/x/y 래스터 타일 폴더 → MBTiles 변환기 (표준 라이브러리만 사용).

입력:  XYZ 레이아웃 폴더  {src}/{z}/{x}/{y}.png   (Google/OSM 방식, y가 북→남 증가)
출력:  MBTiles(SQLite) — 내부는 TMS(y 뒤집힘)로 저장 (MBTiles 스펙)
       → GpsMapTester 의 LocalMBTilesProvider 가 그대로 읽습니다.

사용:
    python folder_to_mbtiles.py  <tiles_dir>  <out.mbtiles>
예:
    python folder_to_mbtiles.py  .\rendered\  .\korea_z15.mbtiles

어떤 렌더러(예: Docker openstreetmap-tile-server, Maperitive, QGIS QMetaTiles 등)로
z/x/y PNG를 만들었든, 이 스크립트로 MBTiles 하나로 묶어 앱에 넣으면 됩니다.
앱 배치 위치:  <exe폴더>\Maps\tiles.mbtiles  (또는 App.config 의 MBTilesPath)
"""
import argparse
import os
import sqlite3
import sys


def build(src: str, out: str) -> None:
    if not os.path.isdir(src):
        sys.exit(f"[ERR] 입력 폴더 없음: {src}")
    if os.path.exists(out):
        os.remove(out)

    con = sqlite3.connect(out)
    cur = con.cursor()
    cur.execute("CREATE TABLE metadata (name text, value text)")
    cur.execute("CREATE TABLE tiles (zoom_level integer, tile_column integer, tile_row integer, tile_data blob)")
    cur.execute("CREATE UNIQUE INDEX tile_index on tiles (zoom_level, tile_column, tile_row)")

    n = 0
    zooms = set()
    exts = (".png", ".jpg", ".jpeg")

    for zdir in sorted(os.listdir(src), key=lambda s: (len(s), s)):
        zpath = os.path.join(src, zdir)
        if not (os.path.isdir(zpath) and zdir.isdigit()):
            continue
        z = int(zdir)
        for xdir in os.listdir(zpath):
            xpath = os.path.join(zpath, xdir)
            if not (os.path.isdir(xpath) and xdir.isdigit()):
                continue
            x = int(xdir)
            for fn in os.listdir(xpath):
                name, ext = os.path.splitext(fn)
                if ext.lower() not in exts or not name.isdigit():
                    continue
                y = int(name)                      # XYZ y (북→남 증가)
                tms_y = (1 << z) - 1 - y            # MBTiles TMS row
                with open(os.path.join(xpath, fn), "rb") as f:
                    data = f.read()
                cur.execute(
                    "INSERT OR REPLACE INTO tiles VALUES (?,?,?,?)",
                    (z, x, tms_y, sqlite3.Binary(data)),
                )
                n += 1
                zooms.add(z)
        if z in zooms:
            print(f"  z{z} ... {n} tiles so far")

    meta = {"name": "GpsMapTester tiles", "format": "png", "type": "baselayer"}
    if zooms:
        meta["minzoom"] = str(min(zooms))
        meta["maxzoom"] = str(max(zooms))
    for k, v in meta.items():
        cur.execute("INSERT INTO metadata VALUES (?,?)", (k, v))

    con.commit()
    con.execute("VACUUM")
    con.close()
    print(f"[OK] {out}  tiles={n}  zooms={sorted(zooms)}")


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description="z/x/y PNG 폴더 → MBTiles")
    ap.add_argument("src", help="타일 폴더 ({z}/{x}/{y}.png)")
    ap.add_argument("out", help="출력 .mbtiles 경로")
    args = ap.parse_args()
    build(args.src, args.out)
