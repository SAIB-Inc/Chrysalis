use crate::eval::utils::{build_utxo_map, create_protocol_params, decode_transaction};
use pallas::codec::minicbor;
use pallas::ledger::primitives::conway::MintedTx;
use pallas::ledger::primitives::conway::TransactionOutput;
use pallas::ledger::primitives::TransactionInput;
use pallas::ledger::traverse::MultiEraTx;
use pallas::ledger::validate::uplc::script_context::SlotConfig;
use pallas::ledger::validate::uplc::tx::eval_tx;
use pallas::ledger::validate::uplc::tx::TxEvalResult;
use pallas::ledger::validate::utils::MultiEraProtocolParameters;

#[derive(minicbor::Decode, minicbor::Encode, Clone)]
pub struct ResolvedInput {
    #[n(0)]
    pub input: TransactionInput,
    #[n(1)]
    pub output: TransactionOutput,
}

#[derive(Debug)]
pub enum EvaluationError {
    DecodingError(String),
    EvaluationError(String),
}

impl std::fmt::Display for EvaluationError {
    fn fmt(&self, f: &mut std::fmt::Formatter) -> std::fmt::Result {
        match self {
            EvaluationError::DecodingError(msg) => write!(f, "Decoding error: {}", msg),
            EvaluationError::EvaluationError(msg) => write!(f, "Evaluation error: {}", msg),
        }
    }
}

impl std::error::Error for EvaluationError {}

pub type EvaluationResult<T> = Result<T, EvaluationError>;

pub struct TransactionEvaluator;

impl TransactionEvaluator {
    pub fn new() -> Self {
        TransactionEvaluator
    }

    pub fn evaluate(
        &self,
        transaction_cbor: &[u8],
        utxo_cbor: &[u8],
    ) -> EvaluationResult<Vec<TxEvalResult>> {
        let mtx: MintedTx = decode_transaction(transaction_cbor)?;
        let metx = MultiEraTx::from_conway(&mtx);

        let resolved_inputs: Vec<ResolvedInput> = minicbor::decode(utxo_cbor).map_err(|e| {
            EvaluationError::DecodingError(format!("Failed to decode UTXOs: {}", e))
        })?;

        let utxos = build_utxo_map(resolved_inputs)?;

        let prot_params = MultiEraProtocolParameters::Conway(create_protocol_params());

        eval_tx(&metx, &prot_params, &utxos, &SlotConfig::default()).map_err(|e| {
            EvaluationError::EvaluationError(format!("Transaction evaluation failed: {}", e))
        })
    }
}

pub fn evaluate_transaction(
    transaction_cbor: &[u8],
    utxo_cbor: &[u8],
) -> EvaluationResult<Vec<TxEvalResult>> {
    let evaluator = TransactionEvaluator::new();
    evaluator.evaluate(transaction_cbor, utxo_cbor)
}
