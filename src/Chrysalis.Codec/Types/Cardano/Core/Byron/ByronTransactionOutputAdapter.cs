using Chrysalis.Codec.Types.Cardano.Core.Transaction;

namespace Chrysalis.Codec.Types.Cardano.Core.Byron;

public class ByronTransactionOutputAdapter(ByronTxOut byronTxOut) : ITransactionOutput
{
    public ByronTxOut ByronTxOut { get; } = byronTxOut;
    public ReadOnlyMemory<byte> Raw { get; } = Serialization.CborSerializer.Serialize(byronTxOut);
    public int ConstrIndex => 0;
    public bool IsIndefinite => false;
}
