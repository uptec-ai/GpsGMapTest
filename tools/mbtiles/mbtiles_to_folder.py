#!/usr/bin/env python3
r"""
MBTiles → z/x/y PNG 폴더 변환기 (앱이 읽는 오프라인 타일 폴더 형식).

앱은 Maps\tiles\{z}\{x}\{y}.png (XYZ, OSM 방식) 폴더를 file:// 로 읽어 오프라인 표출한다.
MBTiles 는 내부적으로 TMS(Y 뒤집힘)라, 이 스크립트가 XYZ 로 풀어 폴더에 저장한다.
표준 라이브러리만 사용.

사용:
    python mbtiles_to_folder.py  <in.mbtiles>  <out_dir>
예:
    python mbtiles_to_folder.py  korea_z12.mbtiles  ..\..\GpsMapTester\Maps\tiles

만든 폴더를 GpsMapTester\Maps\tiles\ 로 두면(또는 교체하면) 빌드 시 출력폴더로 복사되어
완전 오프라인으로 표출된다.
"""
import argparse
import os
import sqlite3
import sys


def convert(src: str, out: str) -> None:
    if not os.path.isfile(src):
        sys.exit(f"[ERR] MBTiles 없음: {src}")
    con = sqlite3.connect(src)
    n = 0
    zooms = set()
    for z, x, tms, data in con.execute(
        "SELECT zoom_level, tile_column, tile_row, tile_data FROM tiles"
    ):
        y = (1 << z) - 1 - tms   # TMS → XYZ
        d = os.path.join(out, str(z), str(x))
        os.makedirs(d, exist_ok=True)
        with open(os.path.join(d, f"{y}.png"), "wb") as f:
            f.write(data)
        n += 1
        zooms.add(z)
    con.close()
    print(f"[OK] {out}  tiles={n}  zooms={sorted(zooms)}")


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description="MBTiles → z/x/y PNG 폴더")
    ap.add_argument("src", help="입력 .mbtiles")
    ap.add_argument("out", help="출력 폴더 (예: GpsMapTester\\Maps\\tiles)")
    args = ap.parse_args()
    convert(args.src, args.out)
