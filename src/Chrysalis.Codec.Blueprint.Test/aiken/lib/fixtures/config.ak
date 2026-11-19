use fixtures/address.{Address, Credential}
use fixtures/policy.{Policy}

pub type Config {
  owner: Policy,
  operators: Option<List<ByteArray>>,
  treasury: Address,
  payout: Address,
  payout_share: (Int, Int),
  stake_keys: List<Credential>,
  guardian: Policy,
  fee_floor: Int,
  fee_step: Int,
  fee_cap: Int,
  pool_fee: Int,
  extra: Data,
}
