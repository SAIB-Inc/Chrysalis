pub type Credential {
  VerificationKey(ByteArray)
  Script(ByteArray)
}

pub type Referenced<a> {
  Inline(a)
  Pointer { slot: Int, tx: Int, cert: Int }
}

pub type Address {
  payment: Credential,
  stake: Option<Referenced<Credential>>,
}

pub type Asset {
  policy: ByteArray,
  name: ByteArray,
}
