"""Stage hash-pinned Pallas codec fixtures for the explicit X8 offline test run."""
import errno
import hashlib
from pathlib import Path
import re
import sys
import tarfile
import tempfile
import urllib.request

PIN = "daecef59a1979d89515524d9583cc28655af182c"  # v1.5.0-rc.2, Apache-2.0
MANIFEST = Path(__file__).with_name("pallas-x8.tsv")


def read_manifest():
    rows = {}
    for line in MANIFEST.read_text(encoding="utf-8").splitlines():
        if line.startswith("#"):
            continue
        digest, name, _, _ = line.split("\t")
        if (not re.fullmatch(r"[a-zA-Z0-9][a-zA-Z0-9_.-]*", name)
                or not re.fullmatch(r"[0-9a-f]{64}", digest) or name in rows):
            raise ValueError(f"Invalid or duplicate fixture: {name}")
        rows[name] = digest
    if not rows:
        raise ValueError("Empty Pallas manifest")
    return rows


def verify(name, data, digest):
    if hashlib.sha256(data).hexdigest() != digest:
        raise ValueError(f"Pallas SHA-256 mismatch: {name}")


def verify_cache(destination, rows):
    for name, digest in rows.items():
        verify(name, (destination / name).read_bytes(), digest)
    print(f"Verified {len(rows)} cached Pallas fixtures at {PIN}")


def stage(destination):
    rows = read_manifest()
    if destination.exists():
        verify_cache(destination, rows)
        return

    destination.parent.mkdir(parents=True, exist_ok=True)
    expected = {f"pallas-{PIN}/test_data/{name}": name for name in rows}
    with tempfile.TemporaryDirectory(dir=destination.parent) as scratch:
        pending = Path(scratch) / "verified"
        pending.mkdir()
        with urllib.request.urlopen(f"https://codeload.github.com/txpipe/pallas/tar.gz/{PIN}", timeout=30) as response:
            with tarfile.open(fileobj=response, mode="r|gz") as archive:
                for member in archive:
                    if member.name not in expected:
                        continue
                    name = expected[member.name]
                    output = pending / name
                    if not member.isfile() or member.size > 2_000_000 or output.exists():
                        raise ValueError(f"Invalid or duplicate Pallas archive member: {name}")
                    data = archive.extractfile(member).read()
                    verify(name, data, rows[name])
                    output.write_bytes(data)
        missing = sorted(name for name in rows if not (pending / name).is_file())
        if missing:
            raise ValueError(f"Missing pinned Pallas fixtures: {', '.join(missing)}")
        try:
            pending.rename(destination)
        except OSError as error:
            if error.errno not in (errno.EEXIST, errno.ENOTEMPTY):
                raise
            # A concurrent stage published first. Never overwrite or trust its contents.
            verify_cache(destination, rows)
            return
    print(f"Fetched and verified {len(rows)} Pallas fixtures at {PIN}")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: FetchX8Pallas.py DESTINATION")
    stage(Path(sys.argv[1]))
