use fixtures/address.{Asset, Credential, Referenced}

pub type Market {
  stake: Referenced<Credential>,
  base: Asset,
  quote: Asset,
  supply: Int,
  base_reserve: Int,
  quote_reserve: Int,
  base_fee: Int,
  quote_fee: Int,
  share_fee: Option<Int>,
  dynamic_fee: Bool,
}
