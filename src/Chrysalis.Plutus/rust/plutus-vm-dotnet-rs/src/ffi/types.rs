use std::os::raw::{c_uint, c_ulong};
use pallas::ledger::validate::uplc::tx::TxEvalResult;

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