use pallas::codec::minicbor::{self, Decode, Encode};
use pallas::ledger::primitives::conway::{
    MintedTx, Redeemer, TransactionOutput
};
use pallas::ledger::primitives::
    TransactionInput
;

use uplc::machine::cost_model::ExBudget;
use uplc::machine::eval_result::EvalResult;
use uplc::tx::error::Error;
use uplc::tx::{eval_phase_two, ResolvedInput, SlotConfig};
use std::os::raw::{c_uint, c_ulong};
use std::slice;

#[repr(C)]
pub struct CTxEvalResult {
    pub tag: u8,
    pub index: c_uint,
    pub memory: c_ulong,
    pub steps: c_ulong,
}

#[repr(C)]
pub struct CTxEvalResultArray {
    ptr: *mut CTxEvalResult,
    len: usize,
}

#[derive(Decode, Encode, Clone)]
pub struct Utxo {
    #[n(0)]
    pub input: TransactionInput,
    #[n(1)]
    pub output: TransactionOutput,
}


impl CTxEvalResultArray {
    fn null() -> Self {
        CTxEvalResultArray {
            ptr: std::ptr::null_mut(),
            len: 0,
        }
    }

    fn from_vec(mut vec: Vec<CTxEvalResult>) -> Self {
        if vec.is_empty() {
            return Self::null();
        }

        let ptr = vec.as_mut_ptr();
        let len = vec.len();

        std::mem::forget(vec);

        CTxEvalResultArray { ptr, len }
    }
}

#[no_mangle]
pub unsafe extern "C" fn eval_tx(
    transaction_cbor_bytes: *const u8,
    transaction_cbor_len: usize,
    resolved_utxo_cbor_bytes: *const u8,
    resolved_utxo_cbor_len: usize,
) -> CTxEvalResultArray {
    let transaction_cbor =
        bytes_from_raw_parts(transaction_cbor_bytes, transaction_cbor_len).unwrap();

    let utxo_cbor = bytes_from_raw_parts(resolved_utxo_cbor_bytes, resolved_utxo_cbor_len).unwrap();

    let c_results = eval(transaction_cbor, utxo_cbor)
        .unwrap()
        .into_iter()
        .map(|result| CTxEvalResult {
            tag: result.0.tag as u8,
            index: result.0.index,
            memory: result.1.cost().mem as u64,
            steps: result.1.cost().cpu as u64,
        })
        .collect();

    CTxEvalResultArray::from_vec(c_results)
}

#[no_mangle]
pub unsafe extern "C" fn free_eval_results(results: *mut CTxEvalResult, len: usize) {
    if !results.is_null() && len > 0 {
        let _ = Vec::from_raw_parts(results, len, len);
    }
}

unsafe fn bytes_from_raw_parts(ptr: *const u8, len: usize) -> Option<&'static [u8]> {
    if ptr.is_null() || len == 0 {
        return None;
    }
    Some(slice::from_raw_parts(ptr, len))
}

fn eval(transaction_cbor: &[u8], utxo_cbor: &[u8]) -> Result<Vec<(Redeemer, EvalResult)>, Error>  {
    let mtx: MintedTx = minicbor::decode(transaction_cbor)
        .unwrap();

    let resolved_inputs: Vec<Utxo> =
        minicbor::decode(utxo_cbor).unwrap();

    let utxos: Vec<ResolvedInput> = resolved_inputs
        .iter()
        .map(|resolved_input| {
            ResolvedInput {
                input: resolved_input.input.clone(),
                output: resolved_input.output.clone(),
            }
        })
        .collect();

    eval_phase_two(&mtx, &utxos, None, Some(&ExBudget::default()), &SlotConfig::default(), false, |_| ()).map_err(|e| e)
}
