# B1 real handshake goldens

Three Haskell node replies captured test-first on 2026-11-16. `manifest.json` records node and source pins, portable commands, timestamps, mux headers, byte counts, hashes, and independently decoded expectations. `upstream-sources.json` pins the CDDL, licences and notices retained under `upstream/`.

- Preprod node 11.1.3: cardano-cli 10.13.1.0 proposed magic 2 to the real magic-1 node; the node returned a CBOR TEXT `Refused` reason at V22.
- Private node 11.2.1 (magic 42): the actual original Chrysalis 7c45d47 assemblies offered V16-V20 and received the node's V23-only `VersionMismatch` list.
- Same private node: its image's Haskell cardano-cli 11.7.0.0 negotiated V23 and the node encoded the accept reply.

Expected fields are decoded with cbor2 5.8.0 (MIT), not an SDK roundtrip. No key or secret is in these files. These captures establish wire facts, not that the production SDK or the full per-version suite passes. Existing G1 goldens separately cover 13 node accepts and both Haskell CLI proposals. The private devnet had only one isolated scratch-directory bind and `--network none`; its final capture session stopped with SIGTERM, exit 0. Setup, earlier failed configuration/signal attempts and transport logs were retained separately, not as fixture dependencies.

Command metadata uses portable placeholders, not machine-specific paths: `<cardano-cli-10.13.1.0>` and `<cardano-cli-11.7.0.0>` identify the separately pinned CLI producers; `<legacy-capture-client-7c45d47.dll>` is the harness referencing the original assemblies identified by their manifest hashes; `<private-node-11.2.1>` identifies the isolated container. `<node-socket>` is the operator-selected transparent capture-proxy endpoint connected to the appropriate node. Resolve these placeholders to local endpoints when reproducing a capture; exact local invocations remain only in noncommitted build evidence. All source/version/binary identity metadata and capture bytes/hashes are retained unchanged.
