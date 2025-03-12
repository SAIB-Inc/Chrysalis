mod eval;
mod ffi;

use ffi::converter::FfiConverter;
use ffi::types::CTxEvalResultArray;

#[no_mangle]
pub unsafe extern "C" fn eval_tx(
    transaction_cbor_bytes: *const u8,
    transaction_cbor_len: usize,
    resolved_utxo_cbor_bytes: *const u8,
    resolved_utxo_cbor_len: usize,
) -> CTxEvalResultArray {
    FfiConverter::evaluate_transaction(
        transaction_cbor_bytes,
        transaction_cbor_len,
        resolved_utxo_cbor_bytes,
        resolved_utxo_cbor_len,
    )
}

#[no_mangle]
pub unsafe extern "C" fn free_tx_results(results: CTxEvalResultArray, len: usize) {
    if !results.ptr.is_null() && len > 0 {
        let _ = Vec::from_raw_parts(results.ptr, len, len);
    }
}
