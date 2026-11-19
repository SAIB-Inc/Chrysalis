"""T1's Conway shapes. Every key generated here is throwaway, including the Byron key."""
import json
import re
from dataclasses import dataclass, field
from functools import cached_property

from capture import REPO, SOURCE, cbor, require, sha

SINCE = "T1"
FEE_IDS = "F01 F01b F03 F04 F05 F06 F07 F08 F09 F10 F11 F11b F12 F12b F13 F14 F15 F16".split()
BOUNDARY_IDS = [f"B{i}" for i in range(1, 7)]
MIN_IDS = [f"M{i:02}" for i in (*range(1, 22), 24, 25, 26, 27)]
CASE_IDS = FEE_IDS + BOUNDARY_IDS + MIN_IDS
G1 = "inputs/g1/current-protocol-parameters.json"
WIRE = "inputs/g1/c1-in-p7-007.cbor"
DERIVED = "inputs/protocol-parameters-derived.json"
UPSTREAM = "inputs/cardano-cli/"
LIMIT = 1 << 32
PAYMENT = 2_000_000
TOTAL = 30_000_000
COLLATERAL = 5_000_000
NETWORK = ("--testnet-magic", 1)
G1_COMMIT = "ee09e2e2f8d1bdaf305852015847653a69ac600b"
G1_PINS = {G1: "66eb079185333cf62ef1efe26c29efce6f32ef6ef17ae44402f67c42fee1b6a7",
           WIRE: "d6fb98127a9055f34ba610c4e9c0afdd71390a79f86365ee495a5deb807b6cc6"}
UPSTREAM_PINS = {
    "LICENSE": "0d542e0c8804e39aa7f37eb00da5a762149dc682d7829451287e11b938e94594",
    "NOTICE": "2a61a15b10e1b920797d2d30d9ef13253b8f44f0bc785fd7320dfe0389286cfd",
    "plutus/v1-always-succeeds.plutus": "704c04469ee259b3d663204d9916374a5b2e5e419e6b43c7d204e4d60e1ff954",
    "plutus/v2-always-succeeds.plutus": "e3bdc6c6734379e4e01d04265e4d65ea6dff085b833ce651aa92c2e372756a40",
    "plutus/v3-always-succeeds.plutus": "8d667d77748a7b0bfcf8e7e93d9a4a54d332bf44f3816c878b922ae9dd5dcd4b",
    "preview-era-history.json": "737240464c5a780b5c4b6b92775e178f0cca06afd281d38c05f060c97ef0c35a",
}


def inputs(ctx):
    src = ctx.into.parents[2]
    preprod = src / "Chrysalis.Network.Tests/Fixtures/Preprod"
    g1 = json.loads((preprod / "manifest.json").read_bytes())
    provenance = {k: g1[k] for k in ("network", "networkMagic", "nodeVersion", "capturedDate")}
    provenance.update(fixture="G1", tipSlot=None, tipSlotReason="G1 does not record a tip slot for these files")
    wire_path = "wire/c1-in-p7-007.cbor"
    wire = next(row for row in g1["wire"] if row["file"] == wire_path)
    for path, name, role, details in (
            (G1, "current-protocol-parameters.json", "parameters", {"tool": "cardano-cli query protocol-parameters"}),
            (WIRE, wire_path, "parameters-wire", {k: wire[k] for k in ("connection", "direction", "protocol", "sequence")})):
        original = preprod / name
        require(sha(original.read_bytes()) == G1_PINS[path], f"G1 input drift: {name}")
        source = dict(repository="Chrysalis", commit=G1_COMMIT, path=f"src/{original.relative_to(src).as_posix()}")
        ctx.link(path, original, role, source=source, since=SINCE, provenance=provenance | details | {"originalPath": source["path"]})
    for name, digest in UPSTREAM_PINS.items():
        path = UPSTREAM + name
        data = (ctx.into / path).read_bytes()
        require(sha(data) == digest, f"upstream fixture drift: {name}")
        attribution = name in {"LICENSE", "NOTICE"}
        source = dict(repository=REPO, commit=SOURCE,
                      path=name if attribution else "cardano-cli/test/cardano-cli-test/files/input/" + name)
        ctx.input(ctx.write(path, data), "attribution" if attribution else "cli-input", source, "Apache-2.0", since=SINCE)


def outref(value, index=0):
    return f"{value:02x}" * 32 + f"#{index}"


def script_file(version):
    return f"{UPSTREAM}plutus/v{version}-always-succeeds.plutus"


def arg(major, number):
    if number < 24:
        return bytes([major * 32 + number])
    code, width = next((i, 1 << i) for i in range(4) if number < 1 << (8 * (1 << i)))
    return bytes([major * 32 + 24 + code]) + number.to_bytes(width, "big")


def native_length(length):
    # An all-of of `after` clauses; varying slot widths fills each exact CBOR length.
    for count in range((length - 3) // 9, (length - 1) // 3 + 1):
        # cardano-ledger-binary's encodeSeq uses an indefinite list above 23 entries.
        overhead = 4 if count > 23 else 3  # all-of wrapper, list header, and optional break
        extra = length - overhead - count * 3
        if 0 <= extra <= count * 8:
            slots = []
            for _ in range(count):
                width = max(n for n in (0, 1, 2, 4, 8) if n <= extra)
                extra -= width
                slots.append({0: 0, 1: 24, 2: 256, 4: 65536, 8: LIMIT}[width])
            if extra == 0:
                return {"type": "all", "scripts": [{"type": "after", "slot": slot} for slot in slots]}
    raise ValueError(f"cannot construct native script of length {length}")


@dataclass
class Output:
    address: str
    assets: dict = field(default_factory=dict)
    flags: tuple = ()

    def args(self, coin):
        value = f"{self.address}+{coin}" + "".join(f"+{quantity} {unit}" for unit, quantity in self.assets.items())
        return ["--tx-out", value, *self.flags]

    def utxo(self, coin, **details):
        value = {"lovelace": coin}
        for unit, amount in self.assets.items():
            policy, _, name = unit.partition(".")
            value.setdefault(policy, {})[name] = amount
        return {"address": self.address, "value": value, "datum": None, "datumhash": None,
                "inlineDatum": None, "referenceScript": None} | details


@dataclass
class Shape:
    spent: dict
    extra: list = field(default_factory=list)
    references: dict = field(default_factory=dict)  # ref -> (UTxO, actual script bytes)
    collateral: dict = field(default_factory=dict)
    pay: Output | None = None
    change: Output | None = None
    withdrawals: int = 0
    deposits: dict = field(default_factory=dict)
    donation: int = 0
    signed: tuple = ()
    spends_scripts: bool = False

    @property
    def total(self):
        return sum(u["value"]["lovelace"] for u in self.spent.values())

    @property
    def available_collateral(self):
        return sum(u["value"]["lovelace"] for u in self.collateral.values())

    @property
    def reference_size(self):
        return sum(size for _, size in self.references.values())


class Fixtures:
    def __init__(self, ctx, *, since):
        self.ctx, self.run, self.since = ctx, ctx.run, since
        self.params = json.loads(ctx.read(G1))
        self.rebuilds = 0

    def key(self, name, *command):
        self.run(*command, "--verification-key-file", f"tmp/{name}.vkey", "--signing-key-file", f"tmp/{name}.skey")

    def address(self, *key):
        return self.run("address", "build", *key, *NETWORK)

    @cached_property
    def payment(self):
        self.key("payment", "address", "key-gen")
        return Output(self.address("--payment-verification-key-file", "tmp/payment.vkey"))

    @cached_property
    def stakeholder(self):
        self.key("stake", "stake-address", "key-gen")
        return self.run("stake-address", "build", "--stake-verification-key-file", "tmp/stake.vkey", *NETWORK)

    @cached_property
    def cold(self):
        self.run("node", "key-gen", "--cold-verification-key-file", "tmp/cold.vkey",
                 "--cold-signing-key-file", "tmp/cold.skey", "--operational-certificate-issue-counter-file", "tmp/cold.counter")
        return "tmp/cold.vkey"

    @cached_property
    def script_key_hash(self):
        self.key("native", "address", "key-gen")
        return self.run("address", "key-hash", "--payment-verification-key-file", "tmp/native.vkey")

    def policy(self, index):
        script = self.ctx.write(f"tmp/policy-{index}.json", {"type": "all", "scripts": [
            {"type": "sig", "keyHash": self.script_key_hash}, {"type": "after", "slot": index + 1}]})
        return self.run("transaction", "policyid", "--script-file", script), script

    def min_shape(self, output, coin, params):
        result = self.run("transaction", "calculate-min-required-utxo", "--protocol-params-file", params, *output.args(coin))
        match = re.fullmatch(r"Coin (\d+)", result)
        require(match is not None, f"unexpected minimum output: {result}")
        return int(match[1])

    def build(self, path, fee, args):
        (self.ctx.root / path).parent.mkdir(exist_ok=True)
        self.run("transaction", "build-raw", *args, "--fee", fee, "--out-file", path)
        return cbor(bytes.fromhex(json.loads(self.ctx.read(path))["cborHex"]))

    def quote(self, path, w, size, params=G1):
        result = self.run("transaction", "calculate-min-fee", "--tx-body-file", path, "--protocol-params-file", params,
                          "--witness-count", w, "--reference-script-size", size, "--output-json")
        return json.loads(result)["fee"]

    def collateral_need(self, fee):
        return (fee * self.params["collateralPercentage"] + 99) // 100

    def transfer(self, total=TOTAL, **kwargs):
        return Shape({outref(0x11): self.payment.utxo(total)}, pay=self.payment, change=self.payment, **kwargs)

    def body(self, tx, shape, fee, *, change=None, returned=None):
        if change is None:
            change = shape.total + shape.withdrawals - PAYMENT - fee - sum(shape.deposits.values()) - shape.donation
        # A script input carries its witness flags next to its --tx-in in extra.
        args = [] if shape.spends_scripts else [part for ref in shape.spent for part in ("--tx-in", ref)]
        args = [*args, *shape.pay.args(PAYMENT), *shape.change.args(change), *shape.extra]
        if shape.collateral:
            available = shape.available_collateral
            returned = available - self.collateral_need(fee) if returned is None else returned
            args += [part for ref in shape.collateral for part in ("--tx-in-collateral", ref)]
            args += ["--tx-out-return-collateral", f"{self.payment.address}+{returned}", "--tx-total-collateral", available - returned]
        raw = "tmp/sign.tx" if shape.signed else tx
        built = self.build(raw, fee, args)
        if shape.signed:
            self.run("transaction", "sign", "--tx-body-file", raw, *shape.signed, *NETWORK, "--out-file", tx)
            built = cbor(bytes.fromhex(json.loads(self.ctx.read(tx))["cborHex"]))
        return args, built

    def verify_rebuild(self, body, args, q):
        rebuilt = self.build("tmp/rebuild.tx", q, args)
        a, b = body.children[0], rebuilt.children[0]
        require(a.major == b.major == 5, "expected a transaction body map")
        fields = lambda m: [item.raw for k, v in zip(m.children[::2], m.children[1::2]) if k.number != 2 for item in (k, v)]
        require(fields(a) == fields(b) and b.key(2).number == q, "fee-changing rebuild altered non-fee fields")
        self.rebuilds += 1

    def fund(self, id, shape):
        entries = shape.spent | {k: v for k, (v, _) in shape.references.items()} | shape.collateral
        return self.ctx.input(self.ctx.write(f"inputs/{id}.utxo.json", entries), "utxo", since=self.since)

    def row(self, id, shape, w, *, variant="", fixed_fee=None, relation=None, change=None, returned=None, utxo=None):
        tx = f"cases/{id}{variant}.tx"
        fee = fixed_fee or 0
        for _ in range(6):
            args, body = self.body(tx, shape, fee, change=change, returned=returned)
            q = self.quote(tx, w, shape.reference_size)
            if q == fee or fixed_fee is not None:
                break
            fee = q
        else:
            raise ValueError(f"{id}: fee did not converge")
        require(body.children[0].key(2).number == fee, f"{id}: fee changed in the built transaction")
        if fee != q:
            self.verify_rebuild(body, args, q)
        if relation:
            require({"fee>quote": fee > q, "fee<quote": fee < q,
                     "collateral<required": fee == q and shape.available_collateral - (returned or 0) < self.collateral_need(fee)}[relation],
                    f"{id}: expected {relation}")
        self.ctx.input(tx, "tx", since=self.since)
        row = dict(kind="candidate" if relation else "balanced", tx=tx, parameters=G1, w=w, s=shape.reference_size,
                   q=q, utxo=utxo or self.fund(id, shape), deposits=shape.deposits)
        return (row | {"relation": relation} if relation else row), (args, body)

    def finish(self, id, about, shape, *witness_reasons, w=None):
        row, _ = self.row(id, shape, len(witness_reasons) if w is None else w)
        self.ctx.case(id, about, [row], witness_reasons, since=self.since)

    def reference(self, script, output, coin, details=None):
        # Extract the actual script span from the CLI's output; UTxO JSON carries the text envelope.
        tx = self.build("tmp/ref.tx", 0, ["--tx-in", outref(1), *Output(output.address, output.assets,
                        ("--tx-out-reference-script-file", script)).args(coin)])
        wrapped = tx.children[0].key(1).children[0].key(3)
        require(wrapped.major == 6 and wrapped.number == 24, "missing tag 24 reference script")
        blob = wrapped.children[0]
        inner = cbor(blob.raw[-blob.number:])
        tag, item = inner.children
        native = tag.number == 0
        if native:
            envelope = {"type": "SimpleScript", "description": "", "cborHex": item.raw.hex()}
        else:
            envelope = json.loads(self.ctx.read(script))
        ref_script = {"scriptLanguage": "SimpleScriptLanguage" if native else "PlutusScriptLanguage PlutusScriptV3", "script": envelope}
        return output.utxo(coin, referenceScript=ref_script, **(details or {})), (len(item.raw) if native else item.number)

    def plutus(self, version, *, count=1, reference=False, units=None):
        script = script_file(version)
        # Use the same detailed-schema datum and raw datum in the UTxO and witness.
        datum = self.ctx.write("tmp/datum.json", {"int": 42})
        datum_hash = self.run("transaction", "hash-script-data", "--script-data-file", datum)
        addr = Output(self.address("--payment-script-file", script))
        if version == 1:
            details = {"datumhash": datum_hash}
        else:
            details = {"inlineDatum": {"int": 42}, "inlineDatumhash": datum_hash, "inlineDatumRaw": "182a"}
        spent = {outref(0x33, i): addr.utxo(TOTAL, **details) for i in range(count)}
        coll = {outref(0xbb): self.payment.utxo(COLLATERAL)}
        refs = {}
        if reference:
            refs[outref(0x44)] = self.reference(script, self.payment, TOTAL)
            require(refs[outref(0x44)][1] == 6, "V3 reference script should have 6 raw bytes")
        elif version == 2:
            refs[outref(0x44)] = (self.payment.utxo(TOTAL), 0)

        def shape(budget):
            extra = ["--protocol-params-file", G1]
            for ref in spent:
                extra += ["--tx-in", ref]
                if reference:
                    extra += ["--spending-tx-in-reference", outref(0x44), "--spending-plutus-script-v3",
                              "--spending-reference-tx-in-inline-datum-present", "--spending-reference-tx-in-redeemer-value", "42",
                              "--spending-reference-tx-in-execution-units", budget]
                else:
                    extra += ["--tx-in-script-file", script]
                    extra += ["--tx-in-datum-file", datum] if version == 1 else ["--tx-in-inline-datum-present"]
                    extra += ["--tx-in-redeemer-value", "42", "--tx-in-execution-units", budget]
            if version == 2:
                extra += ["--read-only-tx-in-reference", outref(0x44)]
            return Shape(spent, extra, refs, coll, self.payment, self.payment, spends_scripts=True)

        if units is None:
            draft = shape("(0,0)")
            temp = self.ctx.write("tmp/eval.utxo.json", spent | {k: v for k, (v, _) in refs.items()} | coll)
            self.body("tmp/eval.tx", draft, 200_000)
            self.run("transaction", "calculate-plutus-script-cost", "offline", "--start-time-posix", 1666656000,
                     "--unsafe-extend-safe-zone", "--era-history-file", UPSTREAM + "preview-era-history.json", "--utxo-file", temp,
                     "--protocol-params-file", G1, "--tx-file", "tmp/eval.tx", "--out-file", "tmp/cost.json")
            samples = json.loads(self.ctx.read("tmp/cost.json"))
            require(len(samples) == 1, "expected one evaluated Plutus spend")
            units = samples[0]["executionUnits"]
        return shape(f"({units['steps']},{units['memory']})")

    def simple(self, selected):
        if selected.intersection({"F01", "F12", "F12b"}):
            shape = self.transfer()
            tx = "cases/F01.tx"
            balanced, body = self.row("F01", shape, 1)

            def quotes(sizes, params=G1, counts=(1,)):
                args, original = body
                rows = []
                for w, size in zip(counts, sizes):
                    q = self.quote(tx, w, size, params)
                    if q != balanced["q"]:
                        self.verify_rebuild(original, args, q)
                    rows.append(dict(kind="quote", tx=tx, parameters=params, w=w, s=size, q=q))
                return rows

            if "F01" in selected:
                extra = quotes((0, 0), counts=(2, 5))
                shape.signed = ("--signing-key-file", "tmp/payment.skey")
                signed, _ = self.row("F01", shape, 0, variant="-signed", utxo=balanced["utxo"])
                require(signed["q"] == balanced["q"], "real VKey and dummy witness quotes differ")
                self.ctx.case("F01", "Transfer; signed control at w=0", [balanced, *extra, signed],
                              ["payment", "w=2 and 5: synthetic counts", "signed: one real payment witness"], since=self.since)
        if "F01b" in selected:
            self.key("byron", "address", "key-gen", "--byron-key")
            byron = self.address("--payment-verification-key-file", "tmp/byron.vkey")
            shape = self.transfer(signed=("--signing-key-file", "tmp/byron.skey", "--address", byron))
            shape.spent = {outref(0x22): Output(byron).utxo(TOTAL)}
            self.finish("F01b", "Transfer with a real bootstrap witness from a throwaway Byron key", shape,
                        "one real bootstrap witness", w=0)
        if "F03" in selected:
            units = [self.policy(i)[0] + ".61" for i in range(3)]
            shape = self.transfer()
            shape.pay, shape.change = (Output(self.payment.address, dict.fromkeys(units, q)) for q in (2, 8))
            shape.spent = {outref(0x11): Output(self.payment.address, dict.fromkeys(units, 10)).utxo(TOTAL)}
            self.finish("F03", "Three policies in payment and change", shape, "payment")
        if "F04" in selected:
            meta = self.ctx.write("tmp/metadata.json", {"674": {"msg": ["synthetic transfer"], "nested": {"counter": 3}}})
            self.finish("F04", "Label 674 and nested metadata", self.transfer(extra=["--metadata-json-file", meta]), "payment")
        if "F05" in selected:
            _, native = self.policy(0)
            ref = outref(0x55)
            script_utxo = Output(self.address("--payment-script-file", native)).utxo(TOTAL)
            shape = self.transfer(extra=["--tx-in", ref, "--tx-in-script-file", native, "--tx-in", outref(0x11),
                                        "--invalid-before", 1, "--invalid-hereafter", 999], spends_scripts=True)
            shape.spent[ref] = script_utxo
            self.finish("F05", "Native all-of signature and time lock", shape, "payment", "native script signature")
        if "F06" in selected:
            policy, native = self.policy(0)
            minted, burned = policy + ".61", policy + ".62"
            shape = self.transfer(extra=["--mint", f"100 {minted}+-5 {burned}", "--mint-script-file", native, "--invalid-before", 1])
            shape.spent = {outref(0x11): Output(self.payment.address, {burned: 10}).utxo(TOTAL)}
            shape.pay = Output(self.payment.address, {minted: 100})
            shape.change = Output(self.payment.address, {burned: 5})
            self.finish("F06", "Mint 100 and burn 5 under one native policy", shape, "payment", "minting policy signature")
        return quotes if selected.intersection({"F01", "F12", "F12b"}) else None

    @cached_property
    def v3_spend(self):
        return self.plutus(3)

    def scripts(self, selected):
        for id, version, about in (("F07", 1, "V1 spend with datum hash and collateral return"),
                                   ("F08", 2, "V2 spend with inline datum and read-only input"),
                                   ("F09", 3, "V3 spend with evaluated units")):
            if id in selected:
                self.finish(id, about, self.v3_spend if version == 3 else self.plutus(version), "collateral key")
        if "F10" in selected:
            self.finish("F10", "V3 reference-script spend at maximum declared execution units",
                        self.plutus(3, reference=True, units=self.params["maxTxExecutionUnits"]), "collateral key")
        for id, sizes in (("F11", (25_700,)), ("F11b", (300, 300, 200))):
            if id not in selected:
                continue
            refs = {}
            for i, size in enumerate(sizes):
                script = native_length(size)
                path = self.ctx.write(f"tmp/native-{size}.json", script)
                refs[outref(0xaa + i)] = self.reference(path, self.payment, TOTAL)
                actual = refs[outref(0xaa + i)][1]
                require(actual == size, f"{id}: expected {size} native script bytes, got {actual}")
            spent = {k: v for i, (k, (v, _)) in enumerate(refs.items()) if i != 1}
            reference_args = [part for k in list(refs)[1:] for part in ("--read-only-tx-in-reference", k)]
            self.finish(id, "Fee counts reference scripts over the union of spent and read-only outrefs",
                        Shape(spent, reference_args, refs, pay=self.payment, change=self.payment), "payment")

    def governance(self, selected):
        p = self.params
        if "F13" in selected:
            self.finish("F13", "Thirty declared V3 redeemers; one ceiling over their summed cost",
                        self.plutus(3, count=30, units={"memory": 500, "steps": 64100}), "collateral key")
        if "F14" in selected:
            deposits = {name: p[name] for name in ("stakeAddressDeposit", "dRepDeposit")}
            withdrawal = self.stakeholder + "+5000000"
            self.key("drep", "governance", "drep", "key-gen")
            self.run("stake-address", "registration-and-delegation-certificate", "--stake-verification-key-file", "tmp/stake.vkey",
                     "--cold-verification-key-file", self.cold, "--key-reg-deposit-amt", deposits["stakeAddressDeposit"],
                     "--out-file", "tmp/stake.cert")
            self.run("governance", "drep", "registration-certificate", "--drep-verification-key-file", "tmp/drep.vkey",
                     "--key-reg-deposit-amt", deposits["dRepDeposit"], "--out-file", "tmp/drep.cert")
            shape = self.transfer(2_000_000_000, deposits=deposits, withdrawals=5_000_000,
                                  extra=["--withdrawal", withdrawal, "--certificate-file", "tmp/stake.cert", "--certificate-file", "tmp/drep.cert"])
            self.finish("F14", "Withdrawal, stake registration and delegation, and DRep registration", shape,
                        "payment", "stake credential", "DRep credential")
        if "F15" in selected:
            _ = self.stakeholder
            self.key("vrf", "node", "key-gen-VRF")
            self.run("stake-pool", "registration-certificate", "--cold-verification-key-file", self.cold,
                     "--vrf-verification-key-file", "tmp/vrf.vkey", "--pool-pledge", 10_000_000,
                     "--pool-cost", p["minPoolCost"], "--pool-margin", "1/20", "--pool-reward-account-verification-key-file",
                     "tmp/stake.vkey", "--pool-owner-stake-verification-key-file", "tmp/stake.vkey", *NETWORK, "--out-file", "tmp/pool.cert")
            self.finish("F15", "Single-owner pool registration", self.transfer(2_000_000_000,
                        deposits={"stakePoolDeposit": p["stakePoolDeposit"]}, extra=["--certificate-file", "tmp/pool.cert"]),
                        "payment", "owner stake credential", "cold key")
        if "F16" in selected:
            deposit = p["govActionDeposit"]
            self.run("governance", "action", "create-info", "--testnet", "--governance-action-deposit", deposit,
                     "--deposit-return-stake-address", self.stakeholder, "--anchor-url", "https://example.invalid/t1",
                     "--anchor-data-hash", "11" * 32, "--out-file", "tmp/info.proposal")
            self.finish("F16", "Info proposal with treasury value and donation", self.transfer(2_000_000_000,
                        deposits={"govActionDeposit": deposit}, donation=100_000,
                        extra=["--proposal-file", "tmp/info.proposal", "--current-treasury-value", 200_000_000, "--treasury-donation", 100_000]),
                        "payment")

    def boundaries(self, selected):
        edge = LIMIT - 1
        for collateral, ids in ((False, ("B1", "B2", "B5")), (True, ("B3", "B4", "B6"))):
            if not selected.intersection(ids):
                continue
            ref = outref(0xbb)
            shape = self.v3_spend if collateral else self.transfer()
            if collateral:
                shape.collateral[ref] = self.payment.utxo(LIMIT + 1_000_000)
            axis = "returned" if collateral else "change"

            def probe(value):
                self.body("tmp/probe.tx", shape, 200_000, **{axis: value})
                return self.quote("tmp/probe.tx", 1, 0)

            q5, q9 = probe(edge), probe(LIMIT)
            require(q9 - q5 == 176, "four-byte boundary should cost 176 lovelace")

            def fund(value):
                if collateral:
                    shape.collateral[ref] = self.payment.utxo(value)
                else:
                    shape.spent[outref(0x11)] = self.payment.utxo(value + PAYMENT)

            witness = ["collateral key" if collateral else "payment"]
            for id, value, q in zip(ids, (edge, LIMIT), (q5, q9)):
                if id in selected:
                    fund(value + (self.collateral_need(q) if collateral else q))
                    row, _ = self.row(id, shape, 1, fixed_fee=q, **{axis: value})
                    require(row["q"] == q, f"{id}: unexpected boundary fee")
                    self.ctx.case(id, f"{axis} is {value}", [row], witness, since=self.since)
            if ids[-1] not in selected:
                continue
            id = ids[-1]
            fund(edge + (self.collateral_need(q5) + 132 if collateral else q5 + 88))
            rows = []
            for index, (value, fee, relation) in enumerate(zip((edge, LIMIT),
                    (q5, q9) if collateral else (q5 + 88, q5 + 87),
                    (None, "collateral<required") if collateral else ("fee>quote", "fee<quote"))):
                prior = rows[0]["utxo"] if rows else None
                row, _ = self.row(id, shape, 1, fixed_fee=fee, variant=f"-{index}", relation=relation, utxo=prior, **{axis: value})
                rows.append(row)
            self.ctx.case(id, f"No exact-fee fixed point with minimum {axis}", rows, witness, since=self.since)

    @cached_property
    def derived(self):
        # For an enterprise output with one asset, changing the name length by one changes the size by one.
        policy = "11" * 28
        middle = Output(self.payment.address, {policy + "." + "01" * 4: 1})
        tx = self.build("tmp/min-derived-probe.tx", 0, ["--tx-in", outref(0x99), *middle.args(1_000_000)])
        size = len(tx.children[0].key(1).children[0].raw)
        changes = {"utxoCostPerByte": (LIMIT - 1) // (160 + size), "minFeeRefScriptCostPerByte": 12.5}
        self.ctx.input(self.ctx.write(DERIVED, self.params | changes), "parameters-derived", since=self.since,
                       derivedFrom={"sha256": G1_PINS[G1], "changes": changes})
        return changes

    def minimum_shapes(self, selected):
        sizes = (23, 24, 255, 256, 65535, 65536)
        policy = "11" * 28
        for id in MIN_IDS:
            if id not in selected:
                continue
            n = int(id[1:])
            address = self.payment.address
            if n == 2:
                _ = self.stakeholder
                address = self.address("--payment-verification-key-file", "tmp/payment.vkey", "--stake-verification-key-file", "tmp/stake.vkey")
            assets, flags, start, label, script_control = {}, (), 0, None, False
            if 3 <= n <= 8:
                names = ["61", "61" * 32] if n <= 4 else [i.to_bytes(2, "big").hex() for i in range((23, 24, 255, 256)[n - 5])]
                assets = {policy + "." + name: 1 for name in ([names[n - 3]] if n <= 4 else names)}
            if n == 9:
                assets = {f"{i:02x}" * 28 + "." + name: 1 for i in range(1, 4) for name in ("61", "62")}
            if n == 10:
                flags = ("--tx-out-datum-hash", "22" * 32)
            if 11 <= n <= 16:
                length = sizes[n - 11]
                path = self.ctx.write(f"tmp/{id}.cbor", bytes([0x9f]) + bytes(length - 2) + bytes([0xff]))
                flags = ("--tx-out-inline-datum-cbor-file", path)
                label = {"layer": "inline-datum", "length": length}
            if 17 <= n <= 21:
                script_control = n == 17
                if script_control:
                    path = script_file(3)
                else:
                    length = sizes[n - 18]
                    script = native_length(length - 2)
                    path = self.ctx.write(f"tmp/{id}.json", script)
                    label = {"layer": "reference-script", "length": length}
                flags = ("--tx-out-reference-script-file", path)
            if n >= 24:
                length = (0, 5, 4, 4)[n - 24]
                assets = {policy + ("." + "01" * length if length else ""): 1}
                start = LIMIT if n == 27 else 0
            yield id, Output(address, assets, flags), start, label, script_control

    def minimums(self, selected):
        for name, params in (("g1", G1), ("derived", DERIVED)):
            is_derived = params == DERIVED
            choices = {id for id in MIN_IDS if id in selected and (int(id[1:]) >= 24) == is_derived}
            if not choices:
                continue
            cost = (self.derived if is_derived else self.params)["utxoCostPerByte"]
            tx, collected = f"cases/M-{name}.tx", []
            arguments = ["--tx-in", outref(0x99)]
            for id, output, start, label, script_control in self.minimum_shapes(choices):
                minimum = self.min_shape(output, start, params)
                arguments += output.args(minimum)
                row = dict(kind="min-utxo", tx=tx, parameters=params, output=len(collected), startCoin=start, minimum=minimum)
                if label:
                    row["label"] = label
                collected.append((id, row, script_control, self.ctx.commands))
                self.ctx.commands = []
            body = self.build(tx, 0, arguments)
            build = self.ctx.commands
            outputs = body.children[0].key(1).children
            self.ctx.input(tx, "tx", since=self.since)
            for id, row, script_control, commands in collected:
                output = outputs[row["output"]]
                require(row["minimum"] == cost * (160 + len(output.raw)), f"{id}: minimum not fixed at output length")
                if row.get("label") or script_control:
                    label = row.get("label") or {"layer": "reference-script", "length": 9}
                    if label["layer"] == "inline-datum":
                        datum = output.key(2)
                        require(datum.major == 4 and len(datum.children) == 2 and datum.children[0].raw == b"\x01",
                                f"{id}: expected inline datum option [1, …]")
                        wrapped = datum.children[1]
                    else:
                        wrapped = output.key(3)
                    length = label["length"]
                    header = b"\xd8\x18" + arg(2, length)
                    require(wrapped.raw.startswith(header) and len(wrapped.raw) == len(header) + length,
                            f"{id}: wrong tag-24 byte-string header or content length")
                build_case = collected[0][0]
                self.ctx.commands = commands + build if id == build_case else commands
                self.ctx.case(id, "CLI minimum coin at the stored indexed output", [row], since=self.since,
                              build_case=None if id == build_case else build_case)
            if is_derived:
                mins = {id: row["minimum"] for id, row, _, _ in collected}
                for id, minimum in mins.items():
                    require((minimum < LIMIT) == (id in ("M24", "M26")), f"{id}: expected opposite side of 2^32")
                if {"M26", "M27"} <= mins.keys():
                    require(mins["M27"] - mins["M26"] == 4 * cost, "initial-coin fixed points should differ by four bytes")


def capture(ctx, selected):
    inputs(ctx)
    fixtures = Fixtures(ctx, since=SINCE)
    quotes = fixtures.simple(selected)
    fixtures.scripts(selected)
    if "F12" in selected:
        sizes = (1, 25599, 25600, 25601, 51200, 51201, 204800)
        ctx.case("F12", "Reference-script price tiers on the transfer", quotes(sizes, counts=(1,) * len(sizes)), ["payment"], since=SINCE)
    if "F12b" in selected:
        _ = fixtures.derived
        sizes = (2, 25600, 25601, 204800)
        rows = quotes(sizes, DERIVED, counts=(1,) * len(sizes))
        base = fixtures.quote("cases/F01.tx", 1, 0)
        require([row["q"] - base for row in rows] == [25, 320000, 320015, 5279707], "wrong fractional-price tier deltas")
        ctx.case("F12b", "Fractional reference-script price tiers on the transfer", rows, ["payment"], since=SINCE)
    fixtures.governance(selected)
    fixtures.boundaries(selected)
    fixtures.minimums(selected)
    if selected == set(CASE_IDS):
        require(fixtures.rebuilds == 15, f"expected 15 fee-changing rebuilds, got {fixtures.rebuilds}")
        print(f"Verified {fixtures.rebuilds} fee-changing rebuilds", flush=True)
