use pallas::codec::minicbor;
use pallas::ledger::primitives::conway::MintedTx;
use pallas::ledger::traverse::MultiEraTx;
use pallas::ledger::validate::uplc::script_context::SlotConfig;
use pallas::ledger::validate::uplc::tx::eval_tx;
use pallas::ledger::validate::uplc::tx::TxEvalResult;
use pallas::ledger::validate::utils::MultiEraProtocolParameters;

use crate::eval::types::{ResolvedInput, EvaluationError, EvaluationResult};
use crate::eval::utils::{decode_transaction, build_utxo_map, create_protocol_params};

pub struct TransactionEvaluator;

impl TransactionEvaluator {
    pub fn new() -> Self {
        TransactionEvaluator
    }
    
    pub fn evaluate(
        &self,
        transaction_cbor: &[u8],
        utxo_cbor: &[u8]
    ) -> EvaluationResult<Vec<TxEvalResult>> {
        let mtx: MintedTx = decode_transaction(transaction_cbor)?;
        let metx = MultiEraTx::from_conway(&mtx);
        
        let resolved_inputs: Vec<ResolvedInput> = minicbor::decode(utxo_cbor)
            .map_err(|e| EvaluationError::DecodingError(
                format!("Failed to decode UTXOs: {}", e)
            ))?;
        
        let utxos = build_utxo_map(resolved_inputs)?;
        
        let prot_params = MultiEraProtocolParameters::Conway(create_protocol_params());
        
        eval_tx(&metx, &prot_params, &utxos, &SlotConfig::default())
            .map_err(|e| EvaluationError::EvaluationError(
                format!("Transaction evaluation failed: {}", e)
            ))
    }
}

pub fn evaluate_transaction(
    transaction_cbor: &[u8],
    utxo_cbor: &[u8]
) -> EvaluationResult<Vec<TxEvalResult>> {
    let evaluator = TransactionEvaluator::new();
    evaluator.evaluate(transaction_cbor, utxo_cbor)
}