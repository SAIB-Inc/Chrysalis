use crate::eval;
use pallas::ledger::validate::uplc::tx::TxEvalResult;
use std::os::raw::{c_uint, c_ulong};
use std::slice;

pub struct FfiConverter;

impl FfiConverter {
    pub unsafe fn bytes_from_ptr(ptr: *const u8, len: usize) -> Option<&'static [u8]> {
        if ptr.is_null() || len == 0 {
            return None;
        }
        Some(slice::from_raw_parts(ptr, len))
    }

    pub fn results_to_c_array(results: Vec<TxEvalResult>) -> CTxEvalResultArray {
        let c_results: Vec<CTxEvalResult> = results.into_iter().map(CTxEvalResult::from).collect();

        CTxEvalResultArray::from_vec(c_results)
    }

    pub unsafe fn evaluate_transaction(
        tx_ptr: *const u8,
        tx_len: usize,
        utxo_ptr: *const u8,
        utxo_len: usize,
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

#[repr(C)]
pub struct CTxEvalResult {
    pub tag: u8,
    pub index: c_uint,
    pub memory: c_ulong,
    pub steps: c_ulong,
}

impl From<TxEvalResult> for CTxEvalResult {
    fn from(result: TxEvalResult) -> Self {
        CTxEvalResult {
            tag: result.tag as u8,
            index: result.index,
            memory: result.units.mem,
            steps: result.units.steps,
        }
    }
}

#[repr(C)]
pub struct CTxEvalResultArray {
    pub ptr: *mut CTxEvalResult,
    pub len: usize,
}

impl CTxEvalResultArray {
    pub fn null() -> Self {
        CTxEvalResultArray {
            ptr: std::ptr::null_mut(),
            len: 0,
        }
    }

    pub fn from_vec(mut vec: Vec<CTxEvalResult>) -> Self {
        if vec.is_empty() {
            return Self::null();
        }

        let ptr = vec.as_mut_ptr();
        let len = vec.len();

        std::mem::forget(vec);

        CTxEvalResultArray { ptr, len }
    }
}
