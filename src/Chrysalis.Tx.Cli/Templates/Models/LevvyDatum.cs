using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Plutus;

namespace Chrysalis.Tx.Cli.Templates.Models;

[CborSerializable]
[CborUnion]
public abstract partial record LevvyDatum : CborBase;

[CborSerializable]
[CborConstr(0)]
public partial record LendDatum(LendDetails LendDetails) : LevvyDatum;

[CborSerializable]
[CborConstr(1)]
public partial record BorrowDatum(BorrowDetails BorrowDetails) : LevvyDatum;

[CborSerializable]
[CborConstr(2)]
public partial record RepayDatum(RepayDetails RepayDetails) : LevvyDatum;

[CborSerializable]
[CborConstr(3)]
public partial record NftPositionDatum(Cip68<LevvyDatum> Cip68NftPosition) : LevvyDatum;