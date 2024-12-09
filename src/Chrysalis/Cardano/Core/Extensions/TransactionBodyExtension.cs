using Chrysalis.Cardano.Core.Types.Block.Transaction.Body;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Input;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Output;
using Chrysalis.Cardano.Core.Utils;
using Chrysalis.Cbor.Types.Collections;

namespace Chrysalis.Cardano.Core.Extensions;

public static class TransactionBodyExtension
{

    public static string Id(this TransactionBody txBody) =>
        Convert.ToHexString(HashUtility.Blake2b256(txBody.Raw ?? [])).ToLowerInvariant();

    public static IEnumerable<TransactionInput> Inputs(this TransactionBody transactionBody)
        => transactionBody switch
        {
            ConwayTransactionBody x => x.Inputs switch
            {

                CborDefList<TransactionInput> list => list.Value,
                CborIndefList<TransactionInput> list => list.Value,
                CborDefListWithTag<TransactionInput> list => list.Value,
                CborIndefListWithTag<TransactionInput> list => list.Value,
                _ => throw new NotImplementedException()
            },
            BabbageTransactionBody x => x.Inputs switch
            {
                CborDefList<TransactionInput> list => list.Value,
                CborIndefList<TransactionInput> list => list.Value,
                CborDefListWithTag<TransactionInput> tagList => tagList.Value,
                CborIndefListWithTag<TransactionInput> tagList => tagList.Value,
                _ => throw new NotImplementedException()
            },
            AlonzoTransactionBody x => x.Inputs switch
            {
                CborDefList<TransactionInput> list => list.Value,
                CborIndefList<TransactionInput> list => list.Value,
                CborDefListWithTag<TransactionInput> tagList => tagList.Value,
                CborIndefListWithTag<TransactionInput> tagList => tagList.Value,
                _ => throw new NotImplementedException()
            },
            _ => throw new NotImplementedException()
        };

    public static IEnumerable<TransactionOutput> Outputs(this TransactionBody transactionBody)
        => transactionBody switch
        {
            ConwayTransactionBody x => x.Outputs switch
            {
                CborDefList<TransactionOutput> list => list.Value,
                CborIndefList<TransactionOutput> list => list.Value,
                CborDefListWithTag<TransactionOutput> list => list.Value,
                CborIndefListWithTag<TransactionOutput> list => list.Value,
                _ => throw new NotImplementedException()
            },
            BabbageTransactionBody x => x.Outputs switch
            {
                CborDefList<TransactionOutput> list => list.Value,
                CborIndefList<TransactionOutput> list => list.Value,
                CborDefListWithTag<TransactionOutput> list => list.Value,
                CborIndefListWithTag<TransactionOutput> list => list.Value,
                _ => throw new NotImplementedException()
            },
            AlonzoTransactionBody x => x.Outputs switch
            {
                CborDefList<TransactionOutput> list => list.Value,
                CborIndefList<TransactionOutput> list => list.Value,
                CborDefListWithTag<TransactionOutput> list => list.Value,
                CborIndefListWithTag<TransactionOutput> list => list.Value,
                _ => throw new NotImplementedException()
            },
            _ => throw new NotImplementedException()
        };

    public static byte[]? AuxiliaryDataHash(this TransactionBody transactionBody)
        => transactionBody switch
        {
            ConwayTransactionBody x => x.AuxiliaryDataHash?.Value,
            BabbageTransactionBody x => x.AuxiliaryDataHash?.Value,
            AlonzoTransactionBody x => x.AuxiliaryDataHash?.Value,
            _ => throw new NotImplementedException()
        };

    public static Dictionary<byte[], TokenBundleMint>? Mint(this TransactionBody transactionBody)
        => transactionBody switch
        {
            ConwayTransactionBody x => x.Mint?.Value.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value),
            BabbageTransactionBody x => x.Mint?.Value.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value),
            AlonzoTransactionBody x => x.Mint?.Value.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value),
            _ => throw new NotImplementedException()
        };
}