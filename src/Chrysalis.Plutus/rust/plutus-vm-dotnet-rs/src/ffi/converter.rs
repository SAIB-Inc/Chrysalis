use std::slice;
use pallas::ledger::validate::uplc::tx::TxEvalResult;
use crate::eval;
use crate::ffi::types::{CTxEvalResult, CTxEvalResultArray};

pub struct FfiConverter;

impl FfiConverter {
    pub unsafe fn bytes_from_ptr(ptr: *const u8, len: usize) -> Option<&'static [u8]> {
        if ptr.is_null() || len == 0 {
            return None;
        }
        Some(slice::from_raw_parts(ptr, len))
    }
    
    pub fn results_to_c_array(results: Vec<TxEvalResult>) -> CTxEvalResultArray {
        let c_results: Vec<CTxEvalResult> = results.into_iter()
            .map(CTxEvalResult::from)
            .collect();
        
        CTxEvalResultArray::from_vec(c_results)
    }
    
    pub unsafe fn evaluate_transaction(
        tx_ptr: *const u8, tx_len: usize,
        utxo_ptr: *const u8, utxo_len: usize
    ) -> CTxEvalResultArray {
        let tx_bytes = match Self::bytes_from_ptr(tx_ptr, tx_len) {
            Some(bytes) => bytes,
            None => return CTxEvalResultArray::null(),
        };
        
        let utxo_bytes = match Self::bytes_from_ptr(utxo_ptr, utxo_len) {
            Some(bytes) => bytes,
            None => return CTxEvalResultArray::null(),
        };
        
        match eval::eval::evaluate_transaction(tx_bytes, utxo_bytes) {
            Ok(results) => Self::results_to_c_array(results),
            Err(_) => CTxEvalResultArray::null(),
        }
    }
}