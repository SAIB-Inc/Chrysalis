use pallas::codec::minicbor::{self, Decode, Encode};
use pallas::ledger::primitives::conway::{
    MintedTx, Redeemer, TransactionOutput
};
use pallas::ledger::primitives::{
    TransactionInput
};

use uplc::machine::cost_model::ExBudget;
use uplc::machine::eval_result::EvalResult;
use uplc::tx::error::Error;
use uplc::tx::{apply_params_to_script, eval_phase_two, ResolvedInput, SlotConfig};
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
    network_type: u32,
) -> CTxEvalResultArray {
    let transaction_cbor = match bytes_from_raw_parts(transaction_cbor_bytes, transaction_cbor_len) {
        Some(bytes) => bytes,
        None => return CTxEvalResultArray::null(),
    };

    let utxo_cbor = match bytes_from_raw_parts(resolved_utxo_cbor_bytes, resolved_utxo_cbor_len) {
        Some(bytes) => bytes,
        None => return CTxEvalResultArray::null(),
    };

    let results = eval(transaction_cbor, utxo_cbor, network_type).unwrap();
      

    let c_results = results
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

#[no_mangle]
pub unsafe extern "C" fn apply_params_to_script_raw(
    script_cbor_bytes: *const u8,
    script_cbor_len: usize,
    params_cbor_bytes: *const u8,
    params_cbor_len: usize,
    out_len: *mut usize,
) -> *mut u8 {
    let script_cbor = match bytes_from_raw_parts(script_cbor_bytes, script_cbor_len) {
        Some(bytes) => bytes,
        None => {
            if !out_len.is_null() {
                *out_len = 0;
            }
            return std::ptr::null_mut();
        }
    };

    let params_cbor = match bytes_from_raw_parts(params_cbor_bytes, params_cbor_len) {
        Some(bytes) => bytes,
        None => {
            if !out_len.is_null() {
                *out_len = 0;
            }
            return std::ptr::null_mut();
        }
    };

    match apply_params_to_script(params_cbor, script_cbor) {
        Ok(parameterized_script) => {
            let len = parameterized_script.len();
            let mut boxed = parameterized_script.into_boxed_slice();
            let ptr = boxed.as_mut_ptr();
            
            // Prevent the Box from being dropped
            std::mem::forget(boxed);
            
            if !out_len.is_null() {
                *out_len = len;
            }
            
            ptr
        }
        Err(_) => {
            if !out_len.is_null() {
                *out_len = 0;
            }
            std::ptr::null_mut()
        }
    }
}

#[no_mangle]
pub unsafe extern "C" fn free_script_bytes(ptr: *mut u8, len: usize) {
    if !ptr.is_null() && len > 0 {
        let _ = Vec::from_raw_parts(ptr, len, len);
    }
}

unsafe fn bytes_from_raw_parts(ptr: *const u8, len: usize) -> Option<&'static [u8]> {
    if ptr.is_null() || len == 0 {
        return None;
    }
    Some(slice::from_raw_parts(ptr, len))
}

fn eval(transaction_cbor: &[u8], utxo_cbor: &[u8], network_type: u32) -> Result<Vec<(Redeemer, EvalResult)>, Error>  {
    let network_type : NetworkType = network_type.into();

    let slot_config = SlotConfig::for_network(network_type);

    let mtx: MintedTx = minicbor::decode(transaction_cbor)?;

    let resolved_inputs: Vec<Utxo> = minicbor::decode(utxo_cbor)?;

    let utxos: Vec<ResolvedInput> = resolved_inputs
        .iter()
        .map(|resolved_input| {
            ResolvedInput {
                input: resolved_input.input.clone(),
                output: resolved_input.output.clone(),
            }
        })
        .collect();

    eval_phase_two(&mtx, &utxos, None, Some(&ExBudget::default()), &slot_config, false, |_| ()).map_err(|e| e)
}

pub enum NetworkType {
    Testnet = 0,
    Mainnet = 1,
    Preview = 2,
    Preprod = 3,
    Unknown = 4,
}

impl From<u32> for NetworkType {
    fn from(value: u32) -> Self {
        match value {
            0 => NetworkType::Testnet,
            1 => NetworkType::Mainnet,
            2 => NetworkType::Preview,
            3 => NetworkType::Preprod,
            _ => NetworkType::Unknown,
        }
    }
}

pub trait SlotConfigExt {
    fn for_network(network: NetworkType) -> Self;
    fn mainnet() -> Self;
    fn preview() -> Self;
    fn preprod() -> Self;
}

impl SlotConfigExt for SlotConfig {
    fn for_network(network: NetworkType) -> Self {
        match network {
            NetworkType::Testnet => SlotConfig::preview(),
            NetworkType::Mainnet => SlotConfig::default(),
            NetworkType::Preview => SlotConfig::preview(),
            NetworkType::Preprod => SlotConfig::preprod(),
            NetworkType::Unknown => SlotConfig::default(),
        }
    }
    fn preview() -> Self {
        SlotConfig {
            slot_length: 1000,
            zero_time: 1666656000000,
            zero_slot: 0
        }
    }

    fn mainnet() -> Self {
        SlotConfig::default()
    }

    fn preprod() -> Self {
        SlotConfig {
            slot_length: 1000,
            zero_time: 1655769600000,
            zero_slot: 86400
        }
    }
}