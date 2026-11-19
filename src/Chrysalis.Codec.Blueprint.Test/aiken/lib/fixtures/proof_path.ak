pub type Sibling {
  nibble: Int,
  prefix: ByteArray,
  root: ByteArray,
}

pub type Hop {
  Wide { depth: Int, siblings: ByteArray }
  Narrow { depth: Int, sibling: Sibling }
  Tip { depth: Int, key: ByteArray, value: ByteArray }
}
