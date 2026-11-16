"""Lossless platform packaging of approved APNG frames; no visual redesign.

WPF does not play APNG natively. Export composited frames and original durations
into one seekable ZIP per animation. Runtime decodes only a bounded frame cache.
The original APNG and its provenance remain the source of truth.
"""
from pathlib import Path
from PIL import Image
import hashlib
import io
import json
import shutil
import zipfile

def write_entry(archive, name, data):
    entry = zipfile.ZipInfo(name, (1980, 1, 1, 0, 0, 0))
    entry.compress_type = zipfile.ZIP_STORED
    entry.external_attr = 0o644 << 16
    archive.writestr(entry, data)

root = Path(__file__).resolve().parents[1]
source = root / "assets" / "avatars"
dest = root / ".artifacts" / "assets"
dest.mkdir(parents=True, exist_ok=True)
for path in sorted(source.glob("*.png")):
    if not path.stem.endswith("-working"):
        shutil.copy2(path, dest / path.name)
        continue
    target = dest / (path.stem + ".zip")
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    if target.exists():
        with zipfile.ZipFile(target) as archive:
            if json.loads(archive.read("animation.json"))["sourceSha256"] == digest:
                continue
    durations = []
    with Image.open(path) as image, zipfile.ZipFile(target, "w", compression=zipfile.ZIP_STORED) as archive:
        for index in range(image.n_frames):
            image.seek(index)
            # Pillow applies APNG blend/disposal. Each exported PNG is a full canvas.
            frame = image.convert("RGBA")
            durations.append(max(0.01, image.info.get("duration", 1000 / 48) / 1000))
            buffer = io.BytesIO()
            frame.save(buffer, format="PNG")
            write_entry(archive, f"{index:04}.png", buffer.getvalue())
        write_entry(archive, "animation.json", json.dumps({"sourceSha256": digest, "durations": durations}))
shutil.copy2(root / "assets" / "amplifier.ico", dest / "amplifier.ico")
print("Prepared original pose atlases and lossless animation frames.")
