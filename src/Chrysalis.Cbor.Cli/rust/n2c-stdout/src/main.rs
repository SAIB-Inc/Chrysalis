use pallas::{
    ledger::traverse::wellknown::PREVIEW_MAGIC,
    network::{
        facades::NodeClient,
        miniprotocols::{chainsync::NextResponse, Point},
    },
};
use std::env;
use std::time::{Duration, Instant};

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

    let socket_path = "/home/rjlacanlale/cardano/ipc/node.socket";
    let mut client = NodeClient::connect(socket_path, PREVIEW_MAGIC)
        .await
        .unwrap();

    let chain_sync_client = client.chainsync();
    let points = vec![Point::new(slot_number, hash_bytes)];
    chain_sync_client.find_intersect(points).await.unwrap();

    // Performance tracking variables
    let mut second_timer = Instant::now();
    let total_timer = Instant::now();
    let mut blocks_processed = 0;
    let mut total_blocks_processed = 0;
    let mut last_block_no = 0;

    // Tip tracking
    let mut is_at_tip = false;
    let mut idle_timer = Instant::now();
    let mut total_idle_time = Duration::from_secs(0);

    for _ in 0..num_blocks {
        if chain_sync_client.has_agency() {
            let block_result = chain_sync_client.request_next().await.unwrap();

            // Process response based on type
            match block_result {
                NextResponse::RollForward(_b, _) => {
                    // Handle new block
                    blocks_processed += 1;
                    total_blocks_processed += 1;

                    // Get block number (ideally extract from block, but for now just increment)
                    // Replace this with actual block number extraction if available
                    last_block_no += 1;

                    // If we were at tip, we're no longer there
                    if is_at_tip {
                        is_at_tip = false;
                        total_idle_time += idle_timer.elapsed();
                        println!(
                            "Exiting idle state, idle time so far: {:.2}s",
                            total_idle_time.as_secs_f64()
                        );
                    }
                }
                NextResponse::RollBackward(point, _) => {
                    // Handle rollback
                    let slot = point.slot_or_default();
                    println!("ROLLBACK to slot {}", slot);

                    // We're definitely not at tip after rollback
                    if is_at_tip {
                        is_at_tip = false;
                        total_idle_time += idle_timer.elapsed();
                    }
                }
                NextResponse::Await => {
                    // Reached tip
                    println!("Tip Reached!");
                    is_at_tip = true;
                    idle_timer = Instant::now();
                    blocks_processed = 0; // Reset counter to avoid duplicate counting
                }
            }

            // Log every second
            if second_timer.elapsed().as_millis() >= 1000 {
                // Calculate active time
                let active_time =
                    total_timer.elapsed().as_secs_f64() - total_idle_time.as_secs_f64();

                println!(
                    "Processed {} blocks in the last {}ms. Latest block: {} | Total: {} blocks in {:.2}s active time",
                    blocks_processed,
                    second_timer.elapsed().as_millis(),
                    last_block_no,
                    total_blocks_processed,
                    active_time
                );

                blocks_processed = 0;
                second_timer = Instant::now();
            }
        }
    }

    // Print final summary
    let active_time = total_timer.elapsed().as_secs_f64() - total_idle_time.as_secs_f64();

    println!("\n--- Sync Summary ---");
    println!("Total blocks processed: {}", total_blocks_processed);
    println!(
        "Total time: {:.2} seconds",
        total_timer.elapsed().as_secs_f64()
    );
    println!("Active sync time: {:.2} seconds", active_time);
    println!(
        "Idle time at tip: {:.2} seconds",
        total_idle_time.as_secs_f64()
    );
    println!(
        "Overall average rate: {:.2} blocks/sec",
        total_blocks_processed as f64 / active_time
    );
}
