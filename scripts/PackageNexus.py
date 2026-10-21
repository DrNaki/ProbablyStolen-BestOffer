"""Package the Best Offer main file for Nexus Mods."""
from pathlib import Path
import hashlib
import json
import xml.etree.ElementTree as ET
import zipfile

root = Path(__file__).resolve().parents[1]
version = ET.parse(root / "src/BestOffer/BestOffer.csproj").findtext(".//Version")
output = root / "nexus-release" / version
output.mkdir(parents=True, exist_ok=True)

def package(name, payload):
    target = output / name
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for entry_name, data in sorted(payload.items()):
            info = zipfile.ZipInfo(entry_name, date_time=(2000, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0
            archive.writestr(info, data)
    with zipfile.ZipFile(target) as archive:
        assert set(archive.namelist()) == set(payload)
        assert all(archive.read(name) == data for name, data in payload.items())
    sha = hashlib.sha256(target.read_bytes()).hexdigest()
    target.with_name(target.name + ".sha256").write_text(f"{sha}  {name}\n", encoding="utf-8")
    return {"File": name, "Sha256": sha, "Entries": sorted(payload)}

main = {
    "Mods/BestOffer.dll": (root / "dist/BestOffer.dll").read_bytes(),
    "README-English.md": (root / "README.en.md").read_bytes(),
    "更新说明-Release-Notes.md": (root / f"docs/{version}-更新说明.md").read_bytes(),
    "THIRD-PARTY-NOTICES.md": (root / "THIRD-PARTY-NOTICES.md").read_bytes(),
}
results = [package(f"BestOffer-{version}-Nexus.zip", main)]

(output / "manifest.json").write_text(json.dumps({"Version": version,
    "DllSha256": hashlib.sha256(main["Mods/BestOffer.dll"]).hexdigest(),
    "Packages": results}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"Output": str(output), "Version": version, "Packages": results}, ensure_ascii=False, indent=2))
