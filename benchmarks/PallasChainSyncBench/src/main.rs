use pallas::{
    ledger::traverse::{MultiEraBlock, MultiEraHeader},
    network::{
        facades::{NodeClient, PeerClient},
        miniprotocols::{chainsync, Point},
    },
};
use std::env;
use std::time::Instant;

struct Args {
    host: String,
    port: u16,
    magic: u64,
    blocks: usize,
    batch: usize,
    socket: Option<String>,
    slot: Option<u64>,
    hash: Option<String>,
    no_deser: bool,
}

fn parse_args() -> Args {
    let args: Vec<String> = env::args().collect();
    let mut host = "127.0.0.1".to_string();
    let mut port: u16 = 3001;
    let mut magic: u64 = 2;
    let mut blocks: usize = 10000;
    let mut batch: usize = 100;
    let mut socket: Option<String> = None;
    let mut slot: Option<u64> = None;
    let mut hash: Option<String> = None;
    let mut no_deser = false;

    let mut i = 1;
    while i < args.len() {
        match args[i].as_str() {
            "--tcp-host" => { host = args[i + 1].clone(); i += 2; },
            "--tcp-port" => { port = args[i + 1].parse().unwrap(); i += 2; },
            "--magic" => { magic = args[i + 1].parse().unwrap(); i += 2; },
            "--blocks" => { blocks = args[i + 1].parse().unwrap(); i += 2; },
            "--batch" => { batch = args[i + 1].parse().unwrap(); i += 2; },
            "--socket" => { socket = Some(args[i + 1].clone()); i += 2; },
            "--slot" => { slot = Some(args[i + 1].parse().unwrap()); i += 2; },
            "--hash" => { hash = Some(args[i + 1].clone()); i += 2; },
            "--no-deser" => { no_deser = true; i += 1; },
            _ => { i += 1; }
        }
    }

    Args { host, port, magic, blocks, batch, socket, slot, hash, no_deser }
}

fn format_bytes(bytes: f64) -> String {
    if bytes >= 1_073_741_824.0 {
        format!("{:.2} GB", bytes / 1_073_741_824.0)
    } else if bytes >= 1_048_576.0 {
        format!("{:.2} MB", bytes / 1_048_576.0)
    } else if bytes >= 1024.0 {
        format!("{:.1} KB", bytes / 1024.0)
    } else {
        format!("{:.0} B", bytes)
    }
}

#[tokio::main]
async fn main() -> Result<(), Box<dyn std::error::Error>> {
    let args = parse_args();

    if let Some(ref socket_path) = args.socket {
        println!("Pallas ChainSync Benchmark (N2C Unix Socket)");
        println!("  Socket:     {}", socket_path);
    } else {
        let server = format!("{}:{}", args.host, args.port);
        println!("Pallas ChainSync + BlockFetch Benchmark (N2N TCP)");
        println!("  Node:       {}", server);
    }
    println!("  Magic:      {}", args.magic);
    println!("  Target:     {} blocks (batch size {})", args.blocks, args.batch);
    println!();

    if args.socket.is_some() {
        run_n2c(args).await
    } else {
        run_n2n(args).await
    }
}

fn make_start_point(args: &Args) -> Point {
    if let (Some(slot), Some(hash)) = (args.slot, args.hash.as_ref()) {
        let hash_bytes = hex::decode(hash).expect("invalid hex hash");
        Point::Specific(slot, hash_bytes)
    } else {
        Point::Origin
    }
}

async fn run_n2c(args: Args) -> Result<(), Box<dyn std::error::Error>> {
    let socket_path = args.socket.as_ref().unwrap();
    let mut node = NodeClient::connect(socket_path, args.magic).await?;

    let start_point = make_start_point(&args);
    if start_point != Point::Origin {
        println!("  Starting from slot {:?}", args.slot.unwrap());
        println!();
    }
    println!("Connected. Starting sync...");
    println!();

    let (_, _) = node.chainsync().find_intersect(vec![start_point]).await?;

    let total_timer = Instant::now();
    let mut window_timer = Instant::now();

    let mut total_blocks_synced: usize = 0;
    let mut total_bytes_downloaded: u64 = 0;
    let mut window_blocks: usize = 0;
    let mut window_bytes: u64 = 0;
    let mut last_slot: u64 = 0;
    let mut last_block_number: u64 = 0;

    const REPORT_INTERVAL: usize = 1000;

    while total_blocks_synced < args.blocks {
        let next = node.chainsync().request_or_await_next().await?;

        match next {
            chainsync::NextResponse::RollForward(block_content, _tip) => {
                let block_bytes: Vec<u8> = block_content.into();
                let block_len = block_bytes.len();

                if !args.no_deser {
                    match MultiEraBlock::decode(&block_bytes) {
                        Ok(block) => {
                            last_slot = block.slot();
                            last_block_number = block.number();
                        }
                        Err(e) => {
                            eprintln!("WARN: block decode failed ({} bytes): {}", block_len, e);
                        }
                    }
                }

                total_blocks_synced += 1;
                window_blocks += 1;
                total_bytes_downloaded += block_len as u64;
                window_bytes += block_len as u64;

                if total_blocks_synced % REPORT_INTERVAL == 0 {
                    print_progress(
                        &total_timer, &window_timer,
                        last_slot, last_block_number, "N2C",
                        window_blocks, window_bytes, total_bytes_downloaded,
                    );
                    window_timer = Instant::now();
                    window_blocks = 0;
                    window_bytes = 0;
                }
            }
            chainsync::NextResponse::RollBackward(_, _) => continue,
            chainsync::NextResponse::Await => {
                println!("Reached chain tip.");
                break;
            }
        }
    }

    print_summary(&total_timer, total_blocks_synced, last_slot, "N2C", total_bytes_downloaded);

    node.chainsync().send_done().await?;
    node.abort().await;

    Ok(())
}

async fn run_n2n(args: Args) -> Result<(), Box<dyn std::error::Error>> {
    let server = format!("{}:{}", args.host, args.port);
    let mut peer = PeerClient::connect(&server, args.magic).await?;

    let start_point = make_start_point(&args);
    println!("Connected. Starting sync...");
    println!();

    let (_, _) = peer.chainsync().find_intersect(vec![start_point]).await?;

    let total_timer = Instant::now();
    let mut window_timer = Instant::now();

    let mut total_blocks_synced: usize = 0;
    let mut total_bytes_downloaded: u64 = 0;
    let mut window_blocks: usize = 0;
    let mut window_bytes: u64 = 0;
    let mut last_era = "?".to_string();
    let mut last_slot: u64 = 0;
    let mut last_block_number: u64 = 0;

    const REPORT_INTERVAL: usize = 1000;

    while total_blocks_synced < args.blocks {
        let remaining = args.blocks - total_blocks_synced;
        let this_batch = remaining.min(args.batch);

        // Phase 1: ChainSync — collect header Points
        let mut header_batch: Vec<(Point, String, u64, u64)> = Vec::with_capacity(this_batch);

        while header_batch.len() < this_batch {
            let next = peer.chainsync().request_next().await?;

            match next {
                chainsync::NextResponse::RollForward(h, _) => {
                    match extract_point(&h) {
                        Some(info) => header_batch.push(info),
                        None => continue,
                    }
                }
                chainsync::NextResponse::RollBackward(_, _) => continue,
                chainsync::NextResponse::Await => {
                    println!("Reached chain tip.");
                    break;
                }
            }
        }

        if header_batch.is_empty() {
            break;
        }

        // Phase 2: BlockFetch + Deserialize each block
        for (point, era, slot, block_num) in &header_batch {
            let block_data = peer.blockfetch().fetch_single(point.clone()).await?;
            let block_len = block_data.len();

            match MultiEraBlock::decode(&block_data) {
                Ok(_) => {}
                Err(e) => {
                    eprintln!("WARN: block {} slot {} ({} bytes): {}", era, slot, block_len, e);
                }
            }

            total_blocks_synced += 1;
            window_blocks += 1;
            total_bytes_downloaded += block_len as u64;
            window_bytes += block_len as u64;
            last_era = era.clone();
            last_slot = *slot;
            last_block_number = *block_num;

            if total_blocks_synced % REPORT_INTERVAL == 0 {
                print_progress(
                    &total_timer, &window_timer,
                    last_slot, last_block_number, &last_era,
                    window_blocks, window_bytes, total_bytes_downloaded,
                );
                window_timer = Instant::now();
                window_blocks = 0;
                window_bytes = 0;
            }
        }
    }

    print_summary(&total_timer, total_blocks_synced, last_slot, &last_era, total_bytes_downloaded);
    peer.abort().await;

    Ok(())
}

fn print_progress(
    total_timer: &Instant, window_timer: &Instant,
    slot: u64, block_num: u64, era: &str,
    window_blocks: usize, window_bytes: u64, total_bytes: u64,
) {
    let window_elapsed = window_timer.elapsed().as_secs_f64();
    let window_blk_per_sec = window_blocks as f64 / window_elapsed;
    let window_bytes_per_sec = window_bytes as f64 / window_elapsed;

    let elapsed = total_timer.elapsed();
    let h = elapsed.as_secs() / 3600;
    let m = (elapsed.as_secs() % 3600) / 60;
    let s = elapsed.as_secs() % 60;

    println!(
        "[{:02}:{:02}:{:02}] slot {:>10} block {:>8} [{:<10}] | {:>7.1} blk/s | {}/s | {} total",
        h, m, s, slot, block_num, era,
        window_blk_per_sec,
        format_bytes(window_bytes_per_sec),
        format_bytes(total_bytes as f64),
    );
}

fn print_summary(
    total_timer: &Instant, total_blocks: usize,
    last_slot: u64, last_era: &str, total_bytes: u64,
) {
    let total_seconds = total_timer.elapsed().as_secs_f64();
    println!();
    println!("=== Summary ===");
    println!("  Blocks synced:  {}", total_blocks);
    println!("  Last slot:      {}", last_slot);
    println!("  Last era:       {}", last_era);
    println!("  Total time:     {:.1}s", total_seconds);
    println!("  Avg blocks/s:   {:.1}", total_blocks as f64 / total_seconds);
    println!("  Avg throughput: {}/s", format_bytes(total_bytes as f64 / total_seconds));
    println!("  Total data:     {}", format_bytes(total_bytes as f64));
}

fn extract_point(h: &chainsync::HeaderContent) -> Option<(Point, String, u64, u64)> {
    let subtag = h.byron_prefix.map(|(_, size)| size as u8);
    let header = MultiEraHeader::decode(h.variant, subtag, &h.cbor).ok()?;
    let slot = header.slot();
    let hash = header.hash().to_vec();
    let number = header.number();
    let era = era_name(&header);
    Some((Point::Specific(slot, hash), era, slot, number))
}

fn era_name(header: &MultiEraHeader) -> String {
    match header {
        MultiEraHeader::EpochBoundary(_) => "Byron-EBB".to_string(),
        MultiEraHeader::Byron(_) => "Byron".to_string(),
        MultiEraHeader::ShelleyCompatible(_) => "Shelley+".to_string(),
        MultiEraHeader::BabbageCompatible(_) => "Babbage+".to_string(),
    }
}
