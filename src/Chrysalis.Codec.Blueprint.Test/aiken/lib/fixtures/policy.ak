pub type Policy {
  Key { hash: ByteArray }
  All { policies: List<Policy> }
  Any { policies: List<Policy> }
  Quorum { required: Int, policies: List<Policy> }
  After { time: Int }
  Before { time: Int }
}
