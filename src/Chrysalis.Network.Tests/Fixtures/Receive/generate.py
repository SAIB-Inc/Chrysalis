"""Independent RECV1 input vectors. Run manually with Python cbor2; not a test dependency.

BlockFetch MsgBlock = [4, #6.24(bytes .cbor any)]. Sizes refer to inner payload.
No Chrysalis encoder is involved. Invalid CBOR is a mutation of a cbor2 vector.
The mux headers are schedule framing, not asserted encoder golden vectors.
"""
import hashlib
import importlib.metadata
import json
from pathlib import Path
import cbor2

root = Path(__file__).parent
vectors = []
for size in (0, 1, 23, 24, 255, 256, 1000, 65500, 2500000, 5000000):
    payload = bytes(i % 251 for i in range(size))
    body = cbor2.dumps(payload)
    encoded = cbor2.dumps([4, cbor2.CBORTag(24, body)])
    name = f"body-{size}.cbor"
    entry = {"payloadBytes": size, "bytes": len(encoded), "sha256": hashlib.sha256(encoded).hexdigest()}
    if size < 2500000:
        (root / name).write_bytes(encoded)
        entry["file"] = name
    else:
        header_length = len(encoded) - size
        assert encoded[header_length:] == payload
        entry["headerHex"] = encoded[:header_length].hex()
        entry["fill"] = {"kind": "index-modulo", "modulus": 251}
    vectors.append(entry)
(root / "manifest.json").write_text(json.dumps({"generator": "Python cbor2", "version": importlib.metadata.version("cbor2"), "vectors": vectors}, indent=2) + "\n")
