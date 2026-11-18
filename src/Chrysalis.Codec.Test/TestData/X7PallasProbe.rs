use pallas_codec::minicbor;
use pallas_primitives::{alonzo::{AuxiliaryData, Block}, Metadatum, PlutusData};
use std::{env, fs};

fn main() {
    let path = env::args().nth(1).expect("Alonzo block fixture path");
    let source = fs::read_to_string(path).unwrap();
    let raw = hex::decode(source.trim()).unwrap();
    let block: Block<'_> = minicbor::decode(&raw).unwrap();
    let metadata = match &*block.auxiliary_data_set[&3] {
        AuxiliaryData::Shelley(data) => data,
        AuxiliaryData::ShelleyMa(data) => &data.transaction_metadata,
        AuxiliaryData::PostAlonzo(data) => data.metadata.as_ref().unwrap(),
    };
    let Metadatum::Map(label) = &metadata[&721] else { panic!("metadata label 721 is not a map") };
    let pairs: Vec<_> = label.iter().filter_map(|(_, value)| match value {
        Metadatum::Map(policy) => Some(policy),
        _ => None,
    }).flat_map(|policy| policy.iter()).filter(|(key, _)| matches!(key, Metadatum::Text(s) if s == "LoveForCrypto1of25")).collect();
    assert_eq!(pairs.len(), 2);
    for (key, value) in pairs {
        println!("metadata\t{}\t{}", hex::encode(minicbor::to_vec(key).unwrap()), hex::encode(minicbor::to_vec(value).unwrap()));
    }
    let input = hex::decode("a201020103").unwrap();
    let PlutusData::Map(datum) = minicbor::decode::<PlutusData>(&input).unwrap() else { panic!("Plutus datum is not a map") };
    assert_eq!(datum.len(), 2);
    for (key, value) in datum.iter() {
        println!("datum\t{}\t{}", hex::encode(minicbor::to_vec(key).unwrap()), hex::encode(minicbor::to_vec(value).unwrap()));
    }
}
