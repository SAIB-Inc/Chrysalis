"""Record the fixed T10 preprod fixtures. Launch through record-blockfrost.sh."""

import datetime
import hashlib
import http.client
import json
import os
from pathlib import Path
import re
import shutil
import signal
import ssl
import sys
import tempfile


HOST = "cardano-preprod.blockfrost.io"
BASE = "/api/v0"
LIMIT = 1_048_576
ADDRESS = "addr_test1wzpaveyshpnq5fn0ke3uxd329tgd509445eeqd3s2pzmk0qg9xn5l"
REFERENCE = "e812fd90ba58247c07664114a94ca63e2d59a5f7881c88a55a022c46ecb0ac03"
PLUTUS = "83d66490b8660a266fb663c3362a2ad0da3cb5ad339036305045bb3c"
NATIVE = "1187b83cc346331269832af99b3cd1fe3e6c5c929fe2f31784310f49"
TOKEN = re.compile(rb"(mainnet|preprod|preview|ipfs)[a-z0-9]{32}", re.I)
AUTH_NAME = re.compile(rb"project_id", re.I)
JSON_TYPE = re.compile(r"application/json(?:;[ \t]*charset=utf-8)?", re.I | re.ASCII)
HASH = re.compile(r"[0-9a-f]{64}")


class Stop(Exception):
    def __init__(self, code, diagnostic):
        super().__init__(diagnostic)
        self.code = code


def read_key(stream, pattern):
    with stream:
        key = stream.read(256).removesuffix(b"\n").removesuffix(b"\r")
    if re.fullmatch(pattern, key) is None:
        raise Stop(2, "credential unavailable or invalid")
    return key


def on_timeout(_signal, _frame):
    raise TimeoutError()


def transfer(tls, method, path, key):
    conn = http.client.HTTPSConnection(HOST, timeout=30, context=tls)
    signal.alarm(30)
    try:
        headers = {"project_id": key} if key is not None else {}
        data = None
        if method == "POST":
            headers["Content-Type"] = "application/cbor"
            data = b"\0"
        conn.request(method, BASE + "/" + path, body=data, headers=headers)
        response = conn.getresponse()
        return response.status, response.reason, response.getheaders(), response.read(LIMIT + 1)
    finally:
        signal.alarm(0)
        conn.close()


def unique_fields(pairs):
    fields = dict(pairs)
    if len(fields) != len(pairs):
        raise ValueError()
    return fields


def reject_constant(_text):
    raise ValueError()


def accept(number, wanted, key, response):
    status, reason, headers, raw = response
    rejected = Stop(3, f"request {number}: rejected")
    fields = [raw, reason.encode("latin-1")]
    fields.extend(field.encode("latin-1") for pair in headers for field in pair)
    if any(key.lower() in value.lower() or TOKEN.search(value) for value in fields):
        raise rejected
    types = [value for name, value in headers if name.lower() == "content-type"]
    lengths = [value for name, value in headers if name.lower() == "content-length"]
    unexpected = Stop(4, f"request {number}: unexpected answer (status {status if 100 <= status <= 999 else '???'})")
    if (status != wanted or len(raw) > LIMIT or len(types) != 1 or not JSON_TYPE.fullmatch(types[0])
            or (lengths and lengths != [str(len(raw))])):
        raise unexpected
    body, stripped = AUTH_NAME.subn(b"[stripped]", raw)
    try:
        answer = json.loads(body.decode("utf-8"), object_pairs_hook=unique_fields, parse_constant=reject_constant)
        decoded = json.dumps(answer, ensure_ascii=True).encode("ascii")
    except (ValueError, RecursionError):
        raise unexpected from None
    # Alphanumeric tokens and auth names remain literal in the re-encoded JSON.
    if key.lower() in decoded.lower() or TOKEN.search(decoded) or AUTH_NAME.search(decoded):
        raise rejected
    origin = f"recorded, stripped:{stripped}" if stripped else "recorded"
    return body, origin, answer


def checked_file(path, sha):
    try:
        data = path.read_bytes()
    except OSError:
        raise Stop(2, "recording input unavailable") from None
    if hashlib.sha256(data).hexdigest() != sha:
        raise Stop(2, "recording input digest mismatch")
    return data


def dolos_refs():
    golden = Path("src/Chrysalis.Tx.Providers.UtxoRpc.Test/Golden/manifest.tsv")
    data = checked_file(golden, "935ebd95b2962ed510c680337ca6cea6a0361c618aa33332bf7674f3dc36d65d")
    refs = []
    for row in data.decode("utf-8").splitlines():
        fields = row.split("\t")
        if fields[0].endswith(("/read-utxos.pb", "/read-hash-utxos.pb")):
            for ref in fields[4].removeprefix("ReadUtxos ").split(","):
                tx, index = ref.split("#", 1)
                if not HASH.fullmatch(tx) or re.fullmatch(r"[0-9]+", index) is None:
                    raise Stop(2, "invalid pinned Dolos outrefs")
                refs.append((tx, int(index)))
    if len(refs) != 10 or refs[0] != (REFERENCE, 0):
        raise Stop(2, "invalid pinned Dolos outrefs")
    return refs


def checked_hash(value):
    if not isinstance(value, str) or HASH.fullmatch(value) is None:
        raise Stop(4, "request 19: invalid transaction list")
    return value


def metadata_reads(add, answer):
    if not isinstance(answer, list) or len(answer) != 1 or not isinstance(answer[0], dict):
        raise Stop(4, "request 19: invalid transaction list")
    tx = checked_hash(answer[0].get("tx_hash"))
    for number, endpoint in ((20, "metadata/cbor"), (21, "cbor")):
        add(number, "GET", f"txs/{checked_hash(tx)}/{endpoint}", 200)


def publish(target, rows, manifest, receipt, output):
    stage = Path(tempfile.mkdtemp(prefix=".blockfrost-", dir=target.parent))
    try:
        for name, body in rows:
            (stage / name).write_bytes(body)
        (stage / ".gitattributes").write_bytes(b"* -text\n")
        (stage / "manifest.tsv").write_bytes(manifest.encode("ascii"))
        print(receipt, file=output, flush=True)
        if os.path.lexists(target):
            raise Stop(2, "fixture folder already exists")
        stage.rename(target)  # Last fallible step on success; no cleanup after publication.
    except BaseException:
        shutil.rmtree(stage)
        raise


def capture(target, key):
    if os.path.lexists(target):
        raise Stop(2, "fixture folder already exists")
    refs = dolos_refs()
    prior = Path("../../build/notes/tx-prov/T10/recordings")
    address = checked_file(prior / "address-utxos-wzpaveys.json",
                           "ca9cc7df6a325158f6b9f48f3e8517f75b4e93ee29b093ec81a4d5b119e14ad7")
    tx_body = checked_file(prior / f"tx-{REFERENCE}-utxos.json",
                           "8d55fbfe1efd7988e1686699538a75b5220a75dfc3370cb7d2b9c13965a82bd1")
    # Construct the context directly: create_default_context honors SSLKEYLOGFILE.
    tls = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    tls.load_default_certs()
    signal.signal(signal.SIGALRM, on_timeout)
    rows, manifest, receipts = [], [], []

    def add(number, method, path, wanted, *, auth=True, replay=None):
        if number != len(rows) + 1 or number > 25:
            raise Stop(5, "request cap or order violated")
        if replay is None:
            try:
                response = transfer(tls, method, path, key if auth else None)
            except (OSError, http.client.HTTPException, ValueError):
                raise Stop(4, f"request {number}: transfer failed") from None
        else:
            response = wanted, "", [("Content-Type", "application/json")], replay
        body, origin, answer = accept(number, wanted, key, response)
        name = f"{number:02d}.json"
        rows.append((name, body))
        manifest.append("\t".join((name, method, path, str(wanted), "application/json",
                                   hashlib.sha256(body).hexdigest(), origin)))
        receipts.append(f"{number} {method} {path} {wanted}")
        return answer

    tip = add(1, "GET", "blocks/latest", 200)
    if (not isinstance(tip, dict) or any(type(tip.get(k)) is not int or tip[k] < 0 for k in ("slot", "height"))
            or not isinstance(tip.get("hash"), str) or HASH.fullmatch(tip["hash"]) is None):
        raise Stop(4, "request 1: invalid tip")
    # This exact pinned address replay has two items and both script hashes are null.
    add(2, "GET", f"addresses/{ADDRESS}/utxos?order=asc&count=100&page=1", 200, replay=address)
    unspent = 0
    for number, (tx, index) in enumerate(refs, start=3):
        answer = add(number, "GET", f"txs/{tx}/utxos", 200, replay=tx_body if tx == REFERENCE else None)
        try:
            unspent += any(o["output_index"] == index and o["collateral"] is False
                           and "consumed_by_tx" in o and o["consumed_by_tx"] is None for o in answer["outputs"])
        except (KeyError, TypeError):
            raise Stop(4, f"request {number}: invalid outputs") from None
    add(13, "GET", "txs/b7061fcf01fba6a92b5a9bd72b9e29bef99ca6dbcab9bccc9cc3164742c3d035/utxos", 200)
    add(14, "GET", f"scripts/{PLUTUS}", 200)
    add(15, "GET", f"scripts/{PLUTUS}/cbor", 200)
    add(16, "GET", f"scripts/{NATIVE}", 200)
    add(17, "GET", f"scripts/{NATIVE}/json", 200)
    add(18, "GET", "epochs/325/parameters", 200)
    metadata_reads(add, add(19, "GET", "metadata/txs/labels/61284?count=1&page=1&order=asc", 200))
    add(22, "GET", "addresses/addr_test1vrqs5lkppflvzzn7cy98asg20mqs5lkppflvzzn7cy98uyqtjxyvt/utxos?count=100&page=1", 404)
    add(23, "GET", "txs/" + "f" * 64 + "/utxos", 404)
    add(24, "GET", "epochs/latest/parameters", 403, auth=False)
    add(25, "POST", "tx/submit", 400)

    now = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    prelude = [
        f"# utc: {now}", "# network: preprod", f"# base: https://{HOST}{BASE}",
        "# openapi: v0.1.93 sha256=b0f64545d55c459525d31ba681d8134fba95d2ce5452d87a754aa7409d2ca887",
        "# 02.json and 03.json: reused from the 2026-11-19T10:28:09Z preprod capture",
        f"# tip: slot={tip['slot']} height={tip['height']} hash={tip['hash']}",
        "file\tmethod\tpath\tstatus\tcontent-type\tsha256\torigin",
    ]
    receipts.extend(("address reference script: key null (reused 18:28 recording)",
                     f"Dolos outrefs unspent: {unspent}/10 (including reused 18:28 tx recording)",
                     "CIP-36 transaction: found"))
    publish(target, rows, "\n".join(prelude + manifest) + "\n", "\n".join(receipts), sys.stdout)


def self_test():
    import contextlib
    import io

    key = read_key(sys.stdin.buffer, rb"CANARY-test-Check")  # Same read/close path, no live credential.
    if not sys.stdin.buffer.closed:
        raise Stop(1, "stdin-close")

    def response(body=b"{}", status=200, mime="application/json", *, length=None):
        return status, "", [("Content-Type", mime), ("Content-Length", str(len(body) if length is None else length))], body

    def expect(name, action, code, diagnostic):
        log = io.StringIO()
        with contextlib.redirect_stdout(log), contextlib.redirect_stderr(log):
            try:
                action()
            except Stop as error:
                ok = error.code == code and str(error) == diagnostic
            except Exception:
                ok = False
            else:
                ok = code == 0
        if not ok or log.getvalue() or key.lower() in (log.getvalue() + diagnostic).encode().lower():
            raise Stop(1, name)

    for name, r, code in (
        ("body-key", response(b'"' + key.lower() + b'"'), 3),
        ("type-key", response(mime="application/json; " + key.lower().decode()), 3),
        ("escaped-key", response(rb'"\u0043' + key[1:] + b'"'), 3),
        ("key-shape", response(b'"PREPROD' + b"0" * 32 + b'"'), 3),
        ("status", response(status=500), 4),
        ("size", response(length=3), 4),
        ("content-type", response(mime="text/html"), 4),
        ("more-than-one-document", response(b"{}\n{}"), 4),
    ):
        diagnostic = "request 19: rejected" if code == 3 else f"request 19: unexpected answer (status {r[0]})"
        expect(name, lambda: accept(19, 200, key, r), code, diagnostic)
    body, origin, _ = accept(19, 200, key, response(b'{"x":"Project_Id"}', mime="APPLICATION/JSON; Charset=UTF-8"))
    if body != b'{"x":"[stripped]"}' or origin != "recorded, stripped:1":
        raise Stop(1, "clean")
    called = []
    for name, answer in (("multiline-hash", [{"tx_hash": "x\n" + "a" * 64}]),
                         ("extra-result", [{"tx_hash": "a" * 64}] * 2), ("empty-result", []),
                         ("uppercase-hash", [{"tx_hash": "A" * 64}])):
        expect(name, lambda: metadata_reads(lambda *args: called.append(args), answer),
               4, "request 19: invalid transaction list")
        if called:
            raise Stop(1, name)

    with tempfile.TemporaryDirectory(prefix="blockfrost-canary-") as temp:
        target = Path(temp) / "blockfrost"
        read_fd, write_fd = os.pipe()
        os.close(read_fd)
        output = os.fdopen(write_fd, "w")
        try:
            publish(target, [("01.json", b"{}")], "", "closed stdout proof", output)
        except BrokenPipeError:
            if any(Path(temp).iterdir()):
                raise Stop(1, "closed-stdout") from None
        else:
            raise Stop(1, "closed-stdout")
        finally:
            try:
                output.close()
            except BrokenPipeError:
                pass
    print("canary self-test: stdin path and 14 cases passed", flush=True)


def main():
    try:
        if sys.argv[1:] == ["--self-test"]:
            self_test()
        elif len(sys.argv) == 1:
            target = Path(__file__).resolve().with_name("blockfrost")
            capture(target, read_key(sys.stdin.buffer, rb"preprod[a-zA-Z0-9]{32}"))
        else:
            raise Stop(2, "invalid recorder arguments")
    except Stop as error:
        print(error, file=sys.stderr)
        return error.code
    except BrokenPipeError:
        sys.stdout = None  # Do not flush a failed output a second time on interpreter exit.
        print("recorder output closed; nothing published", file=sys.stderr)
        return 4
    except (Exception, KeyboardInterrupt):
        # No exception details or traceback: a malformed response can appear in them.
        print("unexpected answer or recorder failure; nothing published", file=sys.stderr)
        return 4
    return 0


if __name__ == "__main__":
    sys.exit(main())
