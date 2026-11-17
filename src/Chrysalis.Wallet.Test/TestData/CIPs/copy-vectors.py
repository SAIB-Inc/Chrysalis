#!/usr/bin/env python3
"""Copy derived Icarus vectors from the pinned CIPs clone passed as the sole argument."""
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys

CIP_COMMIT = "a4068a577790e7b17d2d6f00f6e54990cfcb7955"
root = Path(__file__).resolve().parent
cip = Path(sys.argv[1])
assert subprocess.check_output(["git", "-C", str(cip), "rev-parse", "HEAD"], text=True).strip() == CIP_COMMIT


def source(path):
    return subprocess.check_output(["git", "-C", str(cip), "show", f"{CIP_COMMIT}:{path}"])


icarus = source("CIP-0003/Icarus.md")
readme = source("CIP-0003/README.md")
authors_section = readme.decode().split("Authors:\n", 1)[1].split("Implementors:", 1)[0]
authors = re.findall(r"^\s+- (.+?) <[^>]+>$", authors_section, re.MULTILINE)
assert len(authors) == 2
vectors = []
for block in re.findall(r"<details>(.*?)</details>", icarus.decode(), re.DOTALL):
    phrase = re.search(r"recovery phrase\s+```\s+([^`]+?)\s+```", block)
    key = re.search(r"master key\s+```\s+([0-9a-f]+)\s+```", block)
    password = re.search(r"\n  passphrase\s+```\s+(.+?) \(as utf8 bytes\)\s+```", block)
    assert phrase and key
    assert password or "<summary>No passphrase</summary>" in block
    vectors.append(dict(phrase=phrase[1], password=password[1] if password else "", expected=key[1]))
assert len(vectors) == 2
path = root / "icarus.json"
path.write_text(json.dumps(vectors, indent=2) + "\n")
source_url = f"https://github.com/cardano-foundation/CIPs/blob/{CIP_COMMIT}/"
manifest = [dict(path=path.name, source=source_url + "CIP-0003/Icarus.md", version=CIP_COMMIT,
                 license="CC-BY-4.0", derived=True, sourceSha256=hashlib.sha256(icarus).hexdigest(),
                 attributionSource=source_url + "CIP-0003/README.md", attributionSourceSha256=hashlib.sha256(readme).hexdigest(),
                 sha256=hashlib.sha256(path.read_bytes()).hexdigest())]
(root / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
(root / "LICENSE.txt").write_bytes(source("LICENSE"))
(root / "NOTICE.txt").write_text(
    "Icarus vectors derived from CIP-0003, Wallet Key Generation.\n"
    + "Authors: " + " and ".join(authors) + ".\n"
    + "Source commit: " + CIP_COMMIT + ".\n"
    + "Examples from CIP-0003/Icarus.md were extracted and reshaped as JSON; input and key values are unchanged.\n"
    + "Licensed under CC-BY-4.0; see LICENSE.txt. Author attribution is from CIP-0003/README.md.\n")
