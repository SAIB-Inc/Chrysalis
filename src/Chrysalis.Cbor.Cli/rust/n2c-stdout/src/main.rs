use pallas::{
    ledger::traverse::{wellknown::PREVIEW_MAGIC, MultiEraBlock},
    network::{
        facades::NodeClient,
        miniprotocols::{chainsync::NextResponse, Point},
    },
};
use std::{
    sync::{
        atomic::{AtomicUsize, Ordering},
        Arc,
    },
    time::Duration,
};
use tokio_postgres::{Client, NoTls};

#[tokio::main]
async fn main() {
    // Initialize the database
    let db_client = match initialize_db().await {
        Ok(client) => Arc::new(client),
        Err(e) => {
            eprintln!("Failed to initialize database: {}", e);
            return;
        }
    };
    println!("Database initialized");

    // Connect to the node
    let socket_path = "/home/rjlacanlale/cardano/ipc/node.socket";
    let mut client = match NodeClient::connect(socket_path, PREVIEW_MAGIC).await {
        Ok(client) => client,
        Err(e) => {
            eprintln!("Failed to connect to node: {}", e);
            return;
        }
    };
    println!("Connected to node");

    // Get the chainsync client
    let chainsync_client = client.chainsync();

    // Define the point
    let point =
        match hex::decode("20a81db38339bf6ee9b1d7e22b22c0ac4d887d332bbf4f3005db4848cd647743") {
            Ok(bytes) => Point::Specific(57371845, bytes),
            Err(e) => {
                eprintln!("Failed to decode hex: {}", e);
                return;
            }
        };

    // Find intersection
    println!("Finding Intersection...");
    match chainsync_client.find_intersect(vec![point]).await {
        Ok(_) => println!("Intersection found"),
        Err(e) => {
            eprintln!("Failed to find intersection: {}", e);
            return;
        }
    }

    // Use atomic counters for block counting and deserialization tracking
    let block_count = Arc::new(AtomicUsize::new(0));
    let deserialized_count = Arc::new(AtomicUsize::new(0));

    // Create a task to periodically print block count and deserialization stats
    let block_count_clone = Arc::clone(&block_count);
    let deserialized_count_clone = Arc::clone(&deserialized_count);

    tokio::spawn(async move {
        loop {
            let blocks = block_count_clone.load(Ordering::Relaxed);
            let deserialized = deserialized_count_clone.load(Ordering::Relaxed);
            println!(
                "Block count: {}, Deserialized: {}, Success rate: {:.2}%",
                blocks,
                deserialized,
                if blocks > 0 {
                    (deserialized as f64 / blocks as f64) * 100.0
                } else {
                    0.0
                }
            );
            block_count_clone.store(0, Ordering::Relaxed);
            deserialized_count_clone.store(0, Ordering::Relaxed);
            tokio::time::sleep(Duration::from_secs(1)).await;
        }
    });

    println!("Starting ChainSync...");

    // Main loop for chain sync
    loop {
        // Request next message
        match chainsync_client.request_next().await {
            Ok(response) => {
                match response {
                    // Handle RollForward
                    NextResponse::RollForward(content, _tip) => {
                        // Deserialize the block
                        match MultiEraBlock::decode(&content.0) {
                            Ok(block) => {
                                block_count.fetch_add(1, Ordering::Relaxed);
                                deserialized_count.fetch_add(1, Ordering::Relaxed);

                                // Extract block data
                                let block_number = block.number() as i64; // Using count as number for simplicity
                                let block_slot = block.slot() as i64;
                                let block_hash = hex::encode(block.hash());

                                // Insert into database and await completion
                                match insert_block(
                                    &db_client,
                                    block_number,
                                    block_slot,
                                    &block_hash,
                                )
                                .await
                                {
                                    Ok(_) => {}
                                    Err(e) => {
                                        eprintln!("Failed to insert block: {}", e);
                                    }
                                }
                            }
                            Err(e) => {
                                eprintln!("Failed to deserialize block: {}", e);
                            }
                        }
                    }
                    NextResponse::RollBackward(point, _) => {
                        println!("Rolling back to {}", point.slot_or_default());
                    }
                    NextResponse::Await => {
                        println!("Tip reached!");
                    }
                }
            }
            Err(e) => {
                eprintln!("Error in request_next: {}", e);
            }
        }
    }
}

async fn initialize_db() -> Result<tokio_postgres::Client, tokio_postgres::Error> {
    // Connect to the database
    let (client, connection) = tokio_postgres::connect(
        "host=localhost dbname=chrysalis-rs user=postgres password=test1234 port=5432",
        NoTls,
    )
    .await?;

    // The connection object performs the actual communication with the database
    tokio::spawn(async move {
        if let Err(e) = connection.await {
            eprintln!("connection error: {}", e);
        }
    });

    // Drop the table if it exists
    client.execute("DROP TABLE IF EXISTS blocks", &[]).await?;

    // Create a fresh table with minimal columns
    client
        .execute(
            "CREATE TABLE blocks (
            block_number BIGINT NOT NULL,
            block_slot BIGINT NOT NULL,
            block_hash VARCHAR(64) NOT NULL,
            timestamp TIMESTAMP NOT NULL DEFAULT NOW()
        )",
            &[],
        )
        .await?;

    Ok(client)
}

async fn insert_block(
    client: &Client,
    block_number: i64,
    block_slot: i64,
    block_hash: &str,
) -> Result<(), tokio_postgres::Error> {
    client
        .execute(
            "INSERT INTO blocks (block_number, block_slot, block_hash) VALUES ($1, $2, $3)",
            &[&block_number, &block_slot, &block_hash],
        )
        .await?;

    Ok(())
}
