use fixtures/address.{Address, Asset}
use fixtures/policy.{Policy}

pub type Auth {
  Key { hash: ByteArray }
  Signed { policy: Policy }
}

pub type Order {
  Delegate { auth: Auth }
  Exchange { offer: (ByteArray, ByteArray, Int), min_out: (ByteArray, ByteArray, Int) }
  Add { amounts: ((ByteArray, ByteArray, Int), (ByteArray, ByteArray, Int)) }
  Remove { shares: (ByteArray, ByteArray, Int) }
  Gift { amounts: ((ByteArray, ByteArray, Int), (ByteArray, ByteArray, Int)) }
  Note { tag: (ByteArray, ByteArray) }
}

pub type Ticket {
  owner: Address,
  memo: Option<ByteArray>,
  fallback: Option<Asset>,
  routes: List<(ByteArray, Int)>,
  limits: List<Option<Int>>,
  weights: List<Int>,
  assets: List<Asset>,
}
