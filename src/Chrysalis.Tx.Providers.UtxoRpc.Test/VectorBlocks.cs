using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;

namespace Chrysalis.Tx.Providers.UtxoRpcTests;

/// <summary>Frames upstream transactions from their original block components, without re-encoding.</summary>
internal static class VectorBlocks
{
    internal static async Task<List<byte[]>> ReadAsync(string name)
    {
        byte[] bytes = Convert.FromHexString((await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Golden/u5c-factory/blocks", name + ".block"))).Trim());
        IBlock block = CborSerializer.Deserialize<BlockWithEra>(bytes).Block;
        List<ITransactionBody> bodies = [.. block.TransactionBodies()];
        List<ITransactionWitnessSet> witnesses = [.. block.TransactionWitnessSets()];
        Dictionary<int, IAuxiliaryData> auxiliary = block.AuxiliaryDataSet();
        List<byte[]> result = [];
        for (int i = 0; i < bodies.Count; i++)
        {
            using MemoryStream tx = new();
            tx.WriteByte(0x84);
            tx.Write(bodies[i].Raw.Span);
            tx.Write(witnesses[i].Raw.Span);
            tx.WriteByte(block.InvalidTransactions()?.Contains(i) == true ? (byte)0xf4 : (byte)0xf5);
            if (auxiliary.TryGetValue(i, out IAuxiliaryData? data)) { tx.Write(data.Raw.Span); }
            else { tx.WriteByte(0xf6); }
            result.Add(tx.ToArray());
        }
        return result;
    }
}
