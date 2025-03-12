// core/types.rs - Rust-native type definitions
use pallas::codec::minicbor;
use pallas::ledger::primitives::TransactionInput;
use pallas::ledger::primitives::conway::TransactionOutput;

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