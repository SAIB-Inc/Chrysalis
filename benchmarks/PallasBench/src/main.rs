use pallas::ledger::traverse::MultiEraBlock;
use pallas::network::{
    facades::NodeClient,
    miniprotocols::{chainsync::NextResponse, Point},
};
use pallas_bench::{block_decode, minicbor_walk};
use serde_json::{json, Value};
use sha2::{Digest, Sha256};
use std::{
    env, fs,
    hint::black_box,
    path::Path,
    time::{Duration, Instant},
};

type Failure = Box<dyn std::error::Error>;

fn check(bytes: &[u8]) -> Value {
    let block = MultiEraBlock::decode(bytes).expect("validate block");
    json!({"slot": block.slot(), "hash": block.hash().to_string(),
        "transactions": block.txs().len(), "sha256": hex::encode(Sha256::digest(bytes))})
}

async fn replay(
    socket: &str,
    blocks: &[Vec<u8>],
    iterations: usize,
) -> Result<(u128, Vec<Value>), Failure> {
    let first = block_decode::decode(&blocks[0]);
    let anchor = Point::Specific(first.slot(), first.hash().as_ref().to_vec());
    let mut client = NodeClient::connect(socket, 1).await?;
    let mut elapsed = 0;
    let mut received: Vec<Vec<u8>> = vec![Vec::new(); blocks.len() - 1];
    let mut checks = Vec::new();
    for pass in 0..=iterations {
        let (point, _) = client
            .chainsync()
            .find_intersect(vec![anchor.clone()])
            .await?;
        if point != Some(anchor.clone()) {
            return Err("wrong intersection".into());
        }
        let start = Instant::now();
        for entry in &mut received {
            match client.chainsync().request_next().await? {
                NextResponse::RollForward(content, _) => *entry = content.0,
                _ => return Err("unexpected replay response".into()),
            }
        }
        let duration = start.elapsed().as_nanos();
        if pass > 0 {
            elapsed += duration;
        }
        checks.clear();
        for (entry, expected) in received.iter().zip(&blocks[1..]) {
            if entry != expected {
                return Err("replay bytes changed".into());
            }
            checks.push(check(entry));
        }
    }
    client.chainsync().send_done().await?;
    client.abort().await;
    Ok((elapsed, checks))
}

#[tokio::main]
async fn main() -> Result<(), Failure> {
    let args: Vec<String> = env::args().collect();
    let root = Path::new(&args[1]);
    let mode = &args[2];
    let iterations: usize = args[3].parse()?;
    let mut paths: Vec<_> = fs::read_dir(root.join("fixtures"))?
        .map(|entry| entry.unwrap().path())
        .filter(|path| {
            path.file_name()
                .unwrap()
                .to_string_lossy()
                .starts_with("block-")
                && path.extension().is_some_and(|x| x == "cbor")
        })
        .collect();
    paths.sort();
    let blocks: Vec<Vec<u8>> = paths
        .into_iter()
        .map(|path| fs::read(path).unwrap())
        .collect();
    if blocks.len() != 30 || iterations == 0 {
        return Err("wrong fixed work".into());
    }
    let items: u64 = blocks
        .iter()
        .map(|b| minicbor_walk::count(b).unwrap())
        .sum();
    let (elapsed, checks) = if mode == "replay" {
        replay(&args[4], &blocks, iterations).await?
    } else {
        let operation = || {
            for bytes in &blocks {
                match mode.as_str() {
                    "typed" => {
                        black_box(block_decode::decode(black_box(bytes)));
                    }
                    "walk" => {
                        black_box(minicbor_walk::count(black_box(bytes)).unwrap());
                    }
                    _ => panic!("unknown comparison mode"),
                }
            }
        };
        let warmup = Instant::now();
        while warmup.elapsed() < Duration::from_secs(1) {
            operation();
        }
        let start = Instant::now();
        for _ in 0..iterations {
            operation();
        }
        let elapsed = start.elapsed().as_nanos();
        (elapsed, blocks.iter().map(|b| check(b)).collect())
    };
    println!(
        "{}",
        json!({"mode": mode, "blocks": checks.len(), "iterations": iterations,
        "items": items, "checks": checks, "elapsed_ns": elapsed, "warmupIterations": if mode == "replay" { 1 } else { 0 }})
    );
    Ok(())
}
