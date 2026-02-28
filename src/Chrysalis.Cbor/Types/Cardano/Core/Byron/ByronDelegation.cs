using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Byron;

/// <summary>
/// Represents a Byron heavyweight delegation certificate.
/// </summary>
/// <param name="Epoch">The epoch number for which the delegation is valid.</param>
/// <param name="Issuer">The public key of the issuer.</param>
/// <param name="Delegate">The public key of the delegate.</param>
/// <param name="Certificate">The delegation certificate signature.</param>
[CborSerializable]
[CborList]
public partial record ByronDlg(
    [CborOrder(0)] ulong Epoch,
    [CborOrder(1)] ReadOnlyMemory<byte> Issuer,
    [CborOrder(2)] ReadOnlyMemory<byte> Delegate,
    [CborOrder(3)] ReadOnlyMemory<byte> Certificate
) : CborBase;

/// <summary>
/// Represents a Byron lightweight delegation certificate with an epoch range.
/// </summary>
/// <param name="EpochRange">The range of epochs for which the delegation is valid.</param>
/// <param name="Issuer">The public key of the issuer.</param>
/// <param name="Delegate">The public key of the delegate.</param>
/// <param name="Certificate">The delegation certificate signature.</param>
[CborSerializable]
[CborList]
public partial record ByronLwdlg(
    [CborOrder(0)] CborMaybeIndefList<ulong> EpochRange,
    [CborOrder(1)] ReadOnlyMemory<byte> Issuer,
    [CborOrder(2)] ReadOnlyMemory<byte> Delegate,
    [CborOrder(3)] ReadOnlyMemory<byte> Certificate
) : CborBase;

/// <summary>
/// Byron block signature encoded as [variant_byte, data].
/// Variant 0: Simple signature (ReadOnlyMemory of byte)
/// Variant 1: Lightweight delegation signature ([lwdlg, signature])
/// Variant 2: Heavy delegation signature ([dlg, signature])
/// </summary>
/// <param name="Variant">The signature variant type.</param>
/// <param name="Data">The encoded signature data.</param>
[CborSerializable]
[CborList]
public partial record ByronBlockSig(
    [CborOrder(0)] int Variant,
    [CborOrder(1)] CborEncodedValue Data
) : CborBase;
