using Chrysalis.Cbor.Types.Cardano.Core.Byron;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Byron;

/// <summary>
/// Extension methods for <see cref="ByronTxIn"/> to convert to the unified TransactionInput type.
/// </summary>
public static class ByronTxInExtensions
{
    /// <summary>
    /// Converts a Byron TxIn [variant, #6.24(cbor([txid, index]))] to a unified TransactionInput.
    /// </summary>
    public static TransactionInput ToTransactionInput(this ByronTxIn self)
    {
        ArgumentNullException.ThrowIfNull(self);

        // Data is CborEncodedValue wrapping tag-24(bstr(CBOR([txid, index])))
        // GetValue() strips tag-24 and returns the inner CBOR bytes
        byte[] innerCbor = self.Data.GetValue();

        CborReader reader = new(innerCbor);
        _ = reader.ReadSize(); // array header
        byte[] txId = reader.ReadByteStringToArray();
        uint index = reader.ReadUInt32();

        return new TransactionInput(txId, index);
    }
}
