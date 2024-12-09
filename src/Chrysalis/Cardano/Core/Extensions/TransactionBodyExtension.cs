

using Chrysalis.Cardano.Core.Types.Block.Transaction.Body;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Input;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Output;
using Chrysalis.Cardano.Core.Utils;
using Chrysalis.Cbor.Converters;
using Chrysalis.Cbor.Types.Collections;

namespace Chrysalis.Cardano.Core.Extensions;

public static class TransactionBodyUtils
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
            CborDefiniteListWithTag<TransactionInput> list => list.Value,
            CborIndefListWithTag<TransactionInput> list => list.Value,
            _ => throw new NotImplementedException()
        },
        BabbageTransactionBody x => x.Inputs switch
        {
            CborDefList<TransactionInput> list => list.Value,
            CborIndefList<TransactionInput> list => list.Value,
            CborDefiniteListWithTag<TransactionInput> tagList => tagList.Value,
            CborIndefListWithTag<TransactionInput> tagList => tagList.Value,
            _ => throw new NotImplementedException()
        },
        AlonzoTransactionBody x => x.Inputs switch
        {
            CborDefList<TransactionInput> list => list.Value,
            CborIndefList<TransactionInput> list => list.Value,
            CborDefiniteListWithTag<TransactionInput> tagList => tagList.Value,
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
                CborDefiniteListWithTag<TransactionOutput> list => list.Value,
                CborIndefListWithTag<TransactionOutput> list => list.Value,
                _ => throw new NotImplementedException()
            },
            BabbageTransactionBody x => x.Outputs switch
            {
                CborDefList<TransactionOutput> list => list.Value,
                CborIndefList<TransactionOutput> list => list.Value,
                CborDefiniteListWithTag<TransactionOutput> list => list.Value,
                CborIndefListWithTag<TransactionOutput> list => list.Value,
                _ => throw new NotImplementedException()
            },
            AlonzoTransactionBody x => x.Outputs switch
            {
                CborDefList<TransactionOutput> list => list.Value,
                CborIndefList<TransactionOutput> list => list.Value,
                CborDefiniteListWithTag<TransactionOutput> list => list.Value,
                CborIndefListWithTag<TransactionOutput> list => list.Value,
                _ => throw new NotImplementedException()
            },
            _ => throw new NotImplementedException()
        };
}