use std::env;
use std::io::{self, Write}; // Added Write for stdout

use pallas::{
    ledger::traverse::wellknown::PREVIEW_MAGIC,
    network::{
        facades::NodeClient,
        miniprotocols::{chainsync::NextResponse, Point},
    },
};

#[tokio::main]
async fn main() {
    // Collect command-line arguments
    let args: Vec<String> = env::args().collect();

    // Check if the correct number of arguments is provided (4 includes the program name)
    if args.len() != 4 {
        eprintln!("Usage: {} <slot_number> <hash_hex> <num_blocks>", args[0]);
        std::process::exit(1);
    }

    // Parse the slot number (u64) from the first argument
    let slot_number: u64 = args[1].parse().expect("Invalid slot number");

    // Parse the hash from hex string to bytes from the second argument
    let hash_bytes = hex::decode(&args[2]).expect("Invalid hash hex");

    // Parse the number of blocks (u32) from the third argument
    let num_blocks: u32 = args[3].parse().expect("Invalid number of blocks");

    let socket_path = "/home/rawriclark/CardanoPreview/pool/txpipe/relay1/ipc/node.socket";
    let mut client = NodeClient::connect(socket_path, PREVIEW_MAGIC)
        .await
        .unwrap();

    let chain_sync_client = client.chainsync();
    let points = vec![Point::new(slot_number, hash_bytes)];
    chain_sync_client.find_intersect(points).await.unwrap();

    let mut stdout = io::stdout();

    for _ in 0..num_blocks {
        if chain_sync_client.has_agency() {
            let block = chain_sync_client.request_next().await.unwrap();
            match block {
                NextResponse::RollBackward(_, _) => {
                }
                NextResponse::RollForward(b, _) => {
                    let len = b.0.len() as u32;
                    let len_bytes = len.to_be_bytes();

                    // Write the length prefix
                    stdout
                        .write_all(&len_bytes)
                        .expect("Failed to write block length to stdout");

                    stdout
                        .write_all(&b.0)
                        .expect("Failed to write block bytes to stdout");
                    stdout.flush().expect("Failed to flush stdout"); // Ensure bytes are written immediately
                }
                NextResponse::Await => {
                }
            }
        }
    }
}
