use pallas::codec::minicbor::{self, Decode, Encode};
use pallas::codec::utils::{CborWrap, KeepRaw, KeyValuePairs};
use pallas::ledger::primitives::conway::{
    CostModels, DRepVotingThresholds, MintedTx, NativeScript, PoolVotingThresholds,
    PseudoDatumOption, PseudoScript, PseudoTransactionOutput, TransactionOutput,
};
use pallas::ledger::primitives::{
    self as pallas_primitives, ExUnits, PlutusData, RationalNumber, TransactionInput,
};
use pallas::ledger::traverse::{Era, MultiEraInput, MultiEraOutput, MultiEraTx};
use pallas::ledger::validate::uplc::script_context::SlotConfig;
use pallas::ledger::validate::uplc::tx::{eval_tx as eval_tx_raw, TxEvalResult};
use pallas::ledger::validate::utils::{
    ConwayProtParams, EraCbor, MultiEraProtocolParameters, TxoRef, UtxoMap,
};
use std::borrow::Cow;
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
pub struct ResolvedInput {
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
            tag: result.tag as u8,
            index: result.index,
            memory: result.units.mem,
            steps: result.units.steps,
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

fn eval(transaction_cbor: &[u8], utxo_cbor: &[u8]) -> Result<Vec<TxEvalResult>, String> {
    let mtx: MintedTx = minicbor::decode(transaction_cbor)
        .map_err(|e| format!("Failed to decode transaction: {}", e))?;

    let metx = MultiEraTx::from_conway(&mtx);

    let resolved_inputs: Vec<ResolvedInput> =
        minicbor::decode(utxo_cbor).map_err(|e| format!("Failed to decode UTXOs: {}", e))?;

    let utxos = build_utxo_map(resolved_inputs);

    let prot_params = MultiEraProtocolParameters::Conway(protocol_params());

    eval_tx_raw(&metx, &prot_params, &utxos, &SlotConfig::default())
        .map_err(|e| format!("Evaluation failed: {}", e))
}

fn build_utxo_map(resolved_inputs: Vec<ResolvedInput>) -> UtxoMap {
    let mut utxos: UtxoMap = UtxoMap::new();

    for resolved_input in resolved_inputs {
        let multi_era_in: MultiEraInput =
            MultiEraInput::AlonzoCompatible(Box::new(Cow::Owned(resolved_input.input)));

        match resolved_input.output {
            PseudoTransactionOutput::Legacy(output) => {
                let multi_era_out: MultiEraOutput =
                    MultiEraOutput::from_alonzo_compatible(&output, Era::Alonzo);
                utxos.insert(TxoRef::from(&multi_era_in), EraCbor::from(multi_era_out));
            }
            PseudoTransactionOutput::PostAlonzo(tx_out) => {
                let mut new_datum_buf: Vec<u8> = Vec::new();

                let datum = match tx_out.datum_option {
                    Some(datum) => match datum {
                        PseudoDatumOption::Data(data) => {
                            let _ = minicbor::encode(data.unwrap(), &mut new_datum_buf);
                            let keep_raw_new_datum: KeepRaw<PlutusData> = Decode::decode(
                                &mut minicbor::Decoder::new(new_datum_buf.as_slice()),
                                &mut (),
                            )
                            .unwrap_or_else(|_| panic!("Failed to decode datum"));
                            Some(PseudoDatumOption::Data(CborWrap(keep_raw_new_datum)))
                        }
                        PseudoDatumOption::Hash(_) => None,
                    },
                    None => None,
                };

                let mut new_script_buf: Vec<u8> = Vec::new();

                let script_ref = match tx_out.script_ref {
                    Some(script_ref) => match script_ref.unwrap() {
                        PseudoScript::NativeScript(script) => {
                            let _ = minicbor::encode(script, &mut new_script_buf);
                            let keep_raw_new_script: KeepRaw<NativeScript> = Decode::decode(
                                &mut minicbor::Decoder::new(&new_script_buf.as_slice()),
                                &mut (),
                            )
                            .unwrap_or_else(|_| panic!("Failed to decode script"));
                            Some(CborWrap(PseudoScript::NativeScript(keep_raw_new_script)))
                        }
                        PseudoScript::PlutusV1Script(script) => {
                            Some(CborWrap(PseudoScript::PlutusV1Script(script)))
                        }
                        PseudoScript::PlutusV2Script(script) => {
                            Some(CborWrap(PseudoScript::PlutusV2Script(script)))
                        }
                        PseudoScript::PlutusV3Script(script) => {
                            Some(CborWrap(PseudoScript::PlutusV3Script(script)))
                        }
                    },
                    None => None,
                };

                let tx_out: pallas_primitives::conway::MintedTransactionOutput =
                    pallas_primitives::conway::PseudoTransactionOutput::PostAlonzo(
                        pallas_primitives::conway::MintedPostAlonzoTransactionOutput {
                            address: tx_out.address,
                            value: tx_out.value.clone(),
                            datum_option: datum,
                            script_ref,
                        },
                    );
                let multi_era_out: MultiEraOutput =
                    MultiEraOutput::Conway(Box::new(Cow::Owned(tx_out)));
                utxos.insert(TxoRef::from(&multi_era_in), EraCbor::from(multi_era_out));
            }
        }
    }

    utxos
}

fn protocol_params() -> ConwayProtParams {
    ConwayProtParams {
        system_start: chrono::DateTime::parse_from_rfc3339("2022-10-25T00:00:00Z").unwrap(),
        epoch_length: 432000,
        slot_length: 1,
        minfee_a: 44,
        minfee_b: 155381,
        max_block_body_size: 90112,
        max_transaction_size: 16384,
        max_block_header_size: 1100,
        key_deposit: 2000000,
        pool_deposit: 500000000,
        maximum_epoch: 18,
        desired_number_of_stake_pools: 500,
        pool_pledge_influence: RationalNumber {
            numerator: 3,
            denominator: 10,
        },
        expansion_rate: RationalNumber {
            numerator: 3,
            denominator: 1000,
        },
        treasury_growth_rate: RationalNumber {
            numerator: 2,
            denominator: 10,
        },
        protocol_version: (7, 0),
        min_pool_cost: 340000000,
        ada_per_utxo_byte: 4310,
        cost_models_for_script_languages: CostModels {
            plutus_v1: Some(vec![
                205665, 812, 1, 1, 1000, 571, 0, 1, 1000, 24177, 4, 1, 1000, 32, 117366, 10475, 4,
                23000, 100, 23000, 100, 23000, 100, 23000, 100, 23000, 100, 23000, 100, 100, 100,
                23000, 100, 19537, 32, 175354, 32, 46417, 4, 221973, 511, 0, 1, 89141, 32, 497525,
                14068, 4, 2, 196500, 453240, 220, 0, 1, 1, 1000, 28662, 4, 2, 245000, 216773, 62,
                1, 1060367, 12586, 1, 208512, 421, 1, 187000, 1000, 52998, 1, 80436, 32, 43249, 32,
                1000, 32, 80556, 1, 57667, 4, 1000, 10, 197145, 156, 1, 197145, 156, 1, 204924,
                473, 1, 208896, 511, 1, 52467, 32, 64832, 32, 65493, 32, 22558, 32, 16563, 32,
                76511, 32, 196500, 453240, 220, 0, 1, 1, 69522, 11687, 0, 1, 60091, 32, 196500,
                453240, 220, 0, 1, 1, 196500, 453240, 220, 0, 1, 1, 806990, 30482, 4, 1927926,
                82523, 4, 265318, 0, 4, 0, 85931, 32, 205665, 812, 1, 1, 41182, 32, 212342, 32,
                31220, 32, 32696, 32, 43357, 32, 32247, 32, 38314, 32, 9462713, 1021, 10,
            ]),

            plutus_v2: Some(vec![
                205665,
                812,
                1,
                1,
                1000,
                571,
                0,
                1,
                1000,
                24177,
                4,
                1,
                1000,
                32,
                117366,
                10475,
                4,
                23000,
                100,
                23000,
                100,
                23000,
                100,
                23000,
                100,
                23000,
                100,
                23000,
                100,
                100,
                100,
                23000,
                100,
                19537,
                32,
                175354,
                32,
                46417,
                4,
                221973,
                511,
                0,
                1,
                89141,
                32,
                497525,
                14068,
                4,
                2,
                196500,
                453240,
                220,
                0,
                1,
                1,
                1000,
                28662,
                4,
                2,
                245000,
                216773,
                62,
                1,
                1060367,
                12586,
                1,
                208512,
                421,
                1,
                187000,
                1000,
                52998,
                1,
                80436,
                32,
                43249,
                32,
                1000,
                32,
                80556,
                1,
                57667,
                4,
                1000,
                10,
                197145,
                156,
                1,
                197145,
                156,
                1,
                204924,
                473,
                1,
                208896,
                511,
                1,
                52467,
                32,
                64832,
                32,
                65493,
                32,
                22558,
                32,
                16563,
                32,
                76511,
                32,
                196500,
                453240,
                220,
                0,
                1,
                1,
                69522,
                11687,
                0,
                1,
                60091,
                32,
                196500,
                453240,
                220,
                0,
                1,
                1,
                196500,
                453240,
                220,
                0,
                1,
                1,
                1159724,
                392670,
                0,
                2,
                806990,
                30482,
                4,
                1927926,
                82523,
                4,
                265318,
                0,
                4,
                0,
                85931,
                32,
                205665,
                812,
                1,
                1,
                41182,
                32,
                212342,
                32,
                31220,
                32,
                32696,
                32,
                43357,
                32,
                32247,
                32,
                38314,
                32,
                20000000000,
                20000000000,
                9462713,
                1021,
                10,
                20000000000,
                0,
                20000000000,
            ]),
            plutus_v3: Some(vec![
                100788, 420, 1, 1, 1000, 173, 0, 1, 1000, 59957, 4, 1, 11183, 32, 201305, 8356, 4,
                16000, 100, 16000, 100, 16000, 100, 16000, 100, 16000, 100, 16000, 100, 100, 100,
                16000, 100, 94375, 32, 132994, 32, 61462, 4, 72010, 178, 0, 1, 22151, 32, 91189,
                769, 4, 2, 85848, 123203, 7305, -900, 1716, 549, 57, 85848, 0, 1, 1, 1000, 42921,
                4, 2, 24548, 29498, 38, 1, 898148, 27279, 1, 51775, 558, 1, 39184, 1000, 60594, 1,
                141895, 32, 83150, 32, 15299, 32, 76049, 1, 13169, 4, 22100, 10, 28999, 74, 1,
                28999, 74, 1, 43285, 552, 1, 44749, 541, 1, 33852, 32, 68246, 32, 72362, 32, 7243,
                32, 7391, 32, 11546, 32, 85848, 123203, 7305, -900, 1716, 549, 57, 85848, 0, 1,
                90434, 519, 0, 1, 74433, 32, 85848, 123203, 7305, -900, 1716, 549, 57, 85848, 0, 1,
                1, 85848, 123203, 7305, -900, 1716, 549, 57, 85848, 0, 1, 955506, 213312, 0, 2,
                270652, 22588, 4, 1457325, 64566, 4, 20467, 1, 4, 0, 141992, 32, 100788, 420, 1, 1,
                81663, 32, 59498, 32, 20142, 32, 24588, 32, 20744, 32, 25933, 32, 24623, 32,
                43053543, 10, 53384111, 14333, 10, 43574283, 26308, 10, 16000, 100, 16000, 100,
                962335, 18, 2780678, 6, 442008, 1, 52538055, 3756, 18, 267929, 18, 76433006, 8868,
                18, 52948122, 18, 1995836, 36, 3227919, 12, 901022, 1, 166917843, 4307, 36, 284546,
                36, 158221314, 26549, 36, 74698472, 36, 333849714, 1, 254006273, 72, 2174038, 72,
                2261318, 64571, 4, 207616, 8310, 4, 1293828, 28716, 63, 0, 1, 1006041, 43623, 251,
                0, 1, 100181, 726, 719, 0, 1, 100181, 726, 719, 0, 1, 100181, 726, 719, 0, 1,
                107878, 680, 0, 1, 95336, 1, 281145, 18848, 0, 1, 180194, 159, 1, 1, 158519, 8942,
                0, 1, 159378, 8813, 0, 1, 107490, 3298, 1, 106057, 655, 1, 1964219, 24520, 3,
            ]),
            unknown: KeyValuePairs::from(vec![]),
        },
        execution_costs: pallas_primitives::ExUnitPrices {
            mem_price: RationalNumber {
                numerator: 577,
                denominator: 10000,
            },
            step_price: RationalNumber {
                numerator: 721,
                denominator: 10000000,
            },
        },
        max_tx_ex_units: ExUnits {
            mem: 14000000,
            steps: 10000000000,
        },
        max_block_ex_units: ExUnits {
            mem: 62000000,
            steps: 40000000000,
        },
        max_value_size: 5000,
        collateral_percentage: 150,
        max_collateral_inputs: 3,
        pool_voting_thresholds: PoolVotingThresholds {
            motion_no_confidence: RationalNumber {
                numerator: 0,
                denominator: 1,
            },
            committee_normal: RationalNumber {
                numerator: 0,
                denominator: 1,
            },
            committee_no_confidence: RationalNumber {
                numerator: 0,
                denominator: 1,
            },
            hard_fork_initiation: RationalNumber {
                numerator: 0,
                denominator: 1,
            },
            security_voting_threshold: RationalNumber {
                numerator: 0,
                denominator: 1,
            },
        },
        drep_voting_thresholds: DRepVotingThresholds {
            motion_no_confidence: RationalNumber {
                numerator: 0,
                denominator: 1,
            },
            committee_normal: RationalNumber {
                numerator: 0,
                denominator: 1,
            },
            committee_no_confidence: RationalNumber {
                numerator: 0,
                denominator: 1,
            },
            update_constitution: RationalNumber {
                numerator: 0,
                denominator: 1,
            },
            hard_fork_initiation: RationalNumber {
                numerator: 0,
                denominator: 1,
            },
            pp_network_group: RationalNumber {
                numerator: 0,
                denominator: 1,
            },
            pp_economic_group: RationalNumber {
                numerator: 0,
                denominator: 1,
            },
            pp_technical_group: RationalNumber {
                numerator: 0,
                denominator: 1,
            },
            pp_governance_group: RationalNumber {
                numerator: 0,
                denominator: 1,
            },
            treasury_withdrawal: RationalNumber {
                numerator: 0,
                denominator: 1,
            },
        },
        min_committee_size: 0,
        committee_term_limit: 0,
        governance_action_validity_period: 0,
        governance_action_deposit: 0,
        drep_deposit: 0,
        drep_inactivity_period: 0,
        minfee_refscript_cost_per_byte: RationalNumber {
            numerator: 0,
            denominator: 1,
        },
    }
}
