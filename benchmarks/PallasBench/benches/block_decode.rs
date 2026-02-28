use criterion::{criterion_group, criterion_main, Criterion};
use pallas::ledger::traverse::MultiEraBlock;
use std::fs;
use std::path::PathBuf;

fn data_dir() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../data")
}

fn load_block(filename: &str) -> Vec<u8> {
    let hex_str = fs::read_to_string(data_dir().join(filename))
        .unwrap_or_else(|e| panic!("Failed to read {filename}: {e}"));
    hex::decode(hex_str.trim()).unwrap_or_else(|e| panic!("Failed to decode hex for {filename}: {e}"))
}

fn bench_block_decode(c: &mut Criterion) {
    let blocks: Vec<(&str, Vec<u8>)> = vec![
        ("Byron1", load_block("byron1.block")),
        ("Byron7", load_block("byron7.block")),
        ("Genesis", load_block("genesis.block")),
        ("Shelley1", load_block("shelley1.block")),
        ("Allegra1", load_block("allegra1.block")),
        ("Mary1", load_block("mary1.block")),
        ("Alonzo1", load_block("alonzo1.block")),
        ("Alonzo14", load_block("alonzo14.block")),
        ("Babbage1", load_block("babbage1.block")),
        ("Babbage9", load_block("babbage9.block")),
        ("Conway1", load_block("conway1.block")),
    ];

    for (name, cbor) in &blocks {
        c.bench_function(name, |b| {
            b.iter(|| {
                let _block = MultiEraBlock::decode(cbor).unwrap();
            });
        });
    }
}

criterion_group!(benches, bench_block_decode);
criterion_main!(benches);
