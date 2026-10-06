import json
import sys
from pathlib import Path

out = Path(sys.argv[1])
width, height, max_iter = 360, 220, 90
pixels = bytearray()

for y in range(height):
    cy = -1.15 + (2.30 * y / (height - 1))
    for x in range(width):
        cx = -2.15 + (3.20 * x / (width - 1))
        zx = zy = 0.0
        i = 0
        while zx*zx + zy*zy <= 4.0 and i < max_iter:
            zx, zy = zx*zx - zy*zy + cx, 2.0*zx*zy + cy
            i += 1
        value = 0 if i == max_iter else int(255 * i / max_iter)
        pixels.append(value)

out.parent.mkdir(parents=True, exist_ok=True)
with out.open("wb") as f:
    f.write(f"P5\n{width} {height}\n255\n".encode("ascii"))
    f.write(pixels)

print(json.dumps({
    "ok": True,
    "experiment": "pure-python-mandelbrot",
    "output": str(out),
    "size": [width, height],
    "iterations": max_iter,
    "bytes": out.stat().st_size
}, ensure_ascii=False))
