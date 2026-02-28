using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Byron;

[CborSerializable]
[CborList]
public partial record ByronDlg(
    [CborOrder(0)] ulong Epoch,
    [CborOrder(1)] byte[] Issuer,
    [CborOrder(2)] byte[] Delegate,
    [CborOrder(3)] byte[] Certificate
) : CborBase;

[CborSerializable]
[CborList]
public partial record ByronLwdlg(
    [CborOrder(0)] CborMaybeIndefList<ulong> EpochRange,
    [CborOrder(1)] byte[] Issuer,
    [CborOrder(2)] byte[] Delegate,
    [CborOrder(3)] byte[] Certificate
) : CborBase;

/// <summary>
/// Byron block signature encoded as [variant_byte, data].
/// Variant 0: Simple signature (byte[])
/// Variant 1: Lightweight delegation signature ([lwdlg, signature])
/// Variant 2: Heavy delegation signature ([dlg, signature])
/// </summary>
[CborSerializable]
[CborList]
public partial record ByronBlockSig(
    [CborOrder(0)] int Variant,
    [CborOrder(1)] CborEncodedValue Data
) : CborBase;
