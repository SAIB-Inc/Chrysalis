use fixtures/market.{Market} as market_types
use fixtures/proof_path.{Hop}

validator market {
  spend(_datum: Option<Market>, _path: List<Hop>, _ref: Data, _tx: Data) { True }
  else(_) { fail }
}
