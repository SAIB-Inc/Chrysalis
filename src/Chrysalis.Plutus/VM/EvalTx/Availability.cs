using Chrysalis.Plutus.Cek;
using static Chrysalis.Plutus.Types.DefaultFunction;
using DefaultFunction = Chrysalis.Plutus.Types.DefaultFunction;
using Version = Chrysalis.Plutus.Types.Version;

namespace Chrysalis.Plutus.VM.EvalTx;

internal static class Availability
{
    internal const int VanRossem = 11;

    // PlutusLedgerApi/Common/Versions.hs. AvailabilityTests compares every tag with both pinned releases.
    private static readonly (int Language, int ProtocolVersion, DefaultFunction First, DefaultFunction Last)[] Introductions =
    [
        (1, 5, AddInteger, MkNilPairData),
        (1, VanRossem, SerialiseData, ScaleValue),
        (2, 7, AddInteger, SerialiseData),
        (2, 8, VerifyEcdsaSecp256k1Signature, VerifySchnorrSecp256k1Signature),
        (2, 10, IntegerToByteString, ByteStringToInteger),
        (2, VanRossem, Bls12_381_G1_Add, Blake2b_224),
        (2, VanRossem, AndByteString, ScaleValue),
        (3, 9, AddInteger, ByteStringToInteger),
        (3, 10, AndByteString, Ripemd_160),
        (3, VanRossem, ExpModInteger, ScaleValue)
    ];

    internal static void Check(int plutusVersion, int protocolVersion)
    {
        if (plutusVersion is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(plutusVersion), plutusVersion, "Supported Plutus languages are V1, V2 and V3.");
        }
        ArgumentOutOfRangeException.ThrowIfNegative(protocolVersion);
        if (protocolVersion > Evaluator.LatestProtocolVersion)
        {
            throw new NotSupportedException($"Protocol version {protocolVersion} is not supported; the evaluator supports up to {Evaluator.LatestProtocolVersion}.");
        }
        int introduced = plutusVersion switch { 1 => 5, 2 => 7, _ => 9 };
        if (protocolVersion < introduced)
        {
            throw new InvalidOperationException($"Plutus V{plutusVersion} is not available before protocol version {introduced}.");
        }
    }

    internal static UInt128 Builtins(int language, int pv)
    {
        UInt128 mask = 0;
        foreach ((int lang, int introduced, DefaultFunction first, DefaultFunction last) in Introductions)
        {
            if (language == lang && pv >= introduced)
            {
                for (int tag = (int)first; tag <= (int)last; tag++)
                {
                    mask |= UInt128.One << tag;
                }
            }
        }
        return mask;
    }

    internal static bool HasProgramVersion(int language, int pv, Version version) =>
        version == new Version(1, 0, 0) || ((language == 3 || pv >= VanRossem) && version == new Version(1, 1, 0));

    internal static Variant VariantFor(int language, int pv) => language == 3
        ? (pv < VanRossem ? Variant.C : Variant.E)
        : pv < 9 ? Variant.A : pv < VanRossem ? Variant.B : Variant.D;

    internal static bool CaseOnConstants(int pv) => pv >= VanRossem;
}
