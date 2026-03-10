using Chrysalis.Codec.V2.Types.Cardano.Core.Byron;
using Chrysalis.Codec.V2.Types.Cardano.Core.Transaction;

namespace Chrysalis.Codec.V2.Extensions.Cardano.Core.Byron;

/// <summary>
/// Extension methods for <see cref="ByronTxIn"/> to convert to the unified TransactionInput type.
/// </summary>
public static class ByronTxInExtensions
{
    /// <summary>
    /// Converts a Byron TxIn [variant, #6.24(cbor([txid, index]))] to a unified TransactionInput.
    /// Only variant 0 (standard spending input) is supported.
    /// </summary>
    /// <param name="self">The Byron transaction input.</param>
    /// <param name="input">The resulting transaction input, or default if the variant is unsupported.</param>
    /// <returns><c>true</c> if the input was successfully converted; <c>false</c> for non-zero variants.</returns>
    public static bool TryToTransactionInput(this ByronTxIn self, out TransactionInput input)
    {
        input = default;

        if (self.Variant != 0)
        {
            return false;
        }

        // Data is CborEncodedValue wrapping tag-24(bstr(CBOR([txid, index])))
        // GetValue() strips tag-24 and returns the inner CBOR bytes
        // The inner CBOR is [txid, index] — exactly TransactionInput's CBOR format
        byte[] innerCbor = self.Data.GetValue();
        input = TransactionInput.Read(innerCbor);
        return true;
    }

    /// <summary>
    /// Converts a Byron TxIn [variant, #6.24(cbor([txid, index]))] to a unified TransactionInput.
    /// Throws for non-zero variants.
    /// </summary>
    public static TransactionInput ToTransactionInput(this ByronTxIn self)
    {
        return TryToTransactionInput(self, out TransactionInput input)
            ? input
            : throw new InvalidOperationException($"Unsupported Byron TxIn variant {self.Variant}.");
    }
}
