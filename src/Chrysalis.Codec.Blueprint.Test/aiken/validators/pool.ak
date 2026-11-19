use fixtures/config.{Config}
use fixtures/pool.{PoolAction, PoolState} as pool_types

validator pool(_config_policy: ByteArray) {
  spend(_datum: Option<PoolState>, _action: PoolAction, _ref: Data, _tx: Data) { True }
  mint(_config: Config, _policy: ByteArray, _tx: Data) { True }
  else(_) { fail }
}
