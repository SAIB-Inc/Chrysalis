using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Plutus.Flat;
using Chrysalis.Plutus.Text;

namespace Chrysalis.Tx.Test;

// Plutus scripts and values shared by Chrysalis.Tx.Test and the devnet integration tests.
internal static class TestScripts
{
    // A V3 minting policy that succeeds only when its redeemer equals the transaction's validity range (TxInfo field 7).
    private const string ValidityRangeProgram = """
        (program 1.0.0
          (lam ctx
            [ (lam fields
                [ (lam info
                    (force
                      [ [ [ (force (builtin ifThenElse))
                            [ [ (builtin equalsData)
                                [ (force (builtin headList))
                                  [ (force (builtin tailList)) [ (force (builtin tailList)) [ (force (builtin tailList))
                                  [ (force (builtin tailList)) [ (force (builtin tailList)) [ (force (builtin tailList))
                                  [ (force (builtin tailList)) info ] ] ] ] ] ] ] ] ]
                              [ (force (builtin headList)) [ (force (builtin tailList)) fields ] ] ] ]
                          (delay (con unit ())) ]
                        (delay (error)) ]))
                  [ (force (force (builtin sndPair))) [ (builtin unConstrData) [ (force (builtin headList)) fields ] ] ] ])
              [ (force (force (builtin sndPair))) [ (builtin unConstrData) ctx ] ] ]))
        """;

    internal static PlutusV3Script ValidityRange { get; } = PlutusV3Script.Create(3, Wrap(ValidityRangeProgram));

    // The Flat encoding of a UPLC program inside a CBOR byte string, as a script carries it.
    internal static byte[] Wrap(string program)
    {
        byte[] flat = FlatEncoder.EncodeProgram(UplcParser.NameToDeBruijn(UplcParser.Parse(program)));
        return flat.Length < 24 ? [(byte)(0x40 | flat.Length), .. flat] : [0x58, checked((byte)flat.Length), .. flat];
    }

    internal static MultiAssetOutput OneToken(string policy) => MultiAssetOutput.Create(
        CborMap<ReadOnlyMemory<byte>, TokenBundleOutput>.Create([new(
            Convert.FromHexString(policy),
            TokenBundleOutput.Create(CborMap<ReadOnlyMemory<byte>, ulong>.Create([new(Convert.FromHexString("00"), 1UL)])))]));

    // The ledger's validity range from fromMs (closed) to untilMs (open), in POSIX milliseconds.
    internal static IPlutusData Range(long fromMs, long untilMs)
    {
        static PlutusConstr Constr(int tag, params IPlutusData[] fields) => PlutusConstr.Create(tag, CborList.Create(fields));
        return Constr(0, Constr(0, Constr(1, PlutusInt64.Create(fromMs)), Constr(1)), Constr(0, Constr(1, PlutusInt64.Create(untilMs)), Constr(0)));
    }
}
