//! Successor to the Criterion row: typed decode of the same 30 G8 blocks.
use pallas::ledger::traverse::MultiEraBlock;

pub fn decode(bytes: &[u8]) -> MultiEraBlock<'_> {
    MultiEraBlock::decode(bytes).expect("typed G8 block decode")
}
