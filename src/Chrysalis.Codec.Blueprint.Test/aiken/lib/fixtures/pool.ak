use fixtures/policy.{Policy}

pub type PoolState {
  id: ByteArray,
  reserves: ((ByteArray, ByteArray, Int), (ByteArray, ByteArray, Int)),
  shares: Int,
  fee_in: Int,
  fee_out: Int,
  admin: Option<Policy>,
  opens_at: Int,
  protocol_cut: Int,
}

pub type PoolAction {
  Pause
  Resume
  Rebalance { amounts: (Int, Option<Int>, Int) }
  Settle { window: Option<(Int, Int)> }
}
