
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Input;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Types.Custom;

namespace Chrysalis.Cbor.Cardano.Extensions;

public static class TransactionBodyExtension
{

    public static string Id(this TransactionBody txBody) =>
        Convert.ToHexString(txBody.Raw?.ToBlake2b256() ?? []).ToLowerInvariant();

    public static IEnumerable<TransactionInput> Inputs(this TransactionBody transactionBody)
        => transactionBody switch
        {
            ConwayTransactionBody x => x.Inputs.Value,
            BabbageTransactionBody x => x.Inputs switch
            {
                CborDefList<TransactionInput> list => list.Value,
                CborIndefList<TransactionInput> list => list.Value,
                CborDefListWithTag<TransactionInput> tagList => tagList.Value,
                CborIndefListWithTag<TransactionInput> tagList => tagList.Value,
                _ => []
            },
            AlonzoTransactionBody x => x.Inputs switch
            {
                CborDefList<TransactionInput> list => list.Value,
                CborIndefList<TransactionInput> list => list.Value,
                CborDefListWithTag<TransactionInput> tagList => tagList.Value,
                CborIndefListWithTag<TransactionInput> tagList => tagList.Value,
                _ => []
            },
            _ => []
        };

    public static IEnumerable<TransactionOutput> Outputs(this TransactionBody transactionBody)
        => transactionBody switch
        {
            ConwayTransactionBody x => x.Outputs switch
            {
                CborDefList<PostAlonzoTransactionOutput> list => list.Value,
                CborIndefList<PostAlonzoTransactionOutput> list => list.Value,
                CborDefListWithTag<PostAlonzoTransactionOutput> list => list.Value,
                CborIndefListWithTag<PostAlonzoTransactionOutput> list => list.Value,
                _ => []
            },
            BabbageTransactionBody x => x.Outputs switch
            {
                CborDefList<TransactionOutput> list => list.Value,
                CborIndefList<TransactionOutput> list => list.Value,
                CborDefListWithTag<TransactionOutput> list => list.Value,
                CborIndefListWithTag<TransactionOutput> list => list.Value,
                _ => []
            },
            AlonzoTransactionBody x => x.Outputs switch
            {
                CborDefList<AlonzoTransactionOutput> list => list.Value,
                CborIndefList<AlonzoTransactionOutput> list => list.Value,
                CborDefListWithTag<AlonzoTransactionOutput> list => list.Value,
                CborIndefListWithTag<AlonzoTransactionOutput> list => list.Value,
                _ => []
            },
            _ => []
        };

    public static IEnumerable<TransactionOutput> OutputsSentToAddress(this TransactionBody transactionBody, string address)
        => transactionBody.Outputs().Where(output => output.Address()?.ToString().ToLowerInvariant() == address.ToLowerInvariant());

    public static IEnumerable<TransactionOutput> OutputsWithDatum<T>(this TransactionBody transactionBody, byte[] datum)
        => transactionBody.Outputs().Where(output => output.Datum()?.SequenceEqual(datum) ?? false);

    public static IEnumerable<string> OutRefs(this TransactionBody transactionBody)
        => transactionBody.Inputs().Select(input => input.OutRef());

    public static byte[]? AuxiliaryDataHash(this TransactionBody transactionBody)
        => transactionBody switch
        {
            ConwayTransactionBody x => x.AuxiliaryDataHash?.Value,
            BabbageTransactionBody x => x.AuxiliaryDataHash?.Value,
            AlonzoTransactionBody x => x.AuxiliaryDataHash?.Value,
            _ => null
        };

    public static Dictionary<byte[], TokenBundleMint>? Mint(this TransactionBody transactionBody)
        => transactionBody switch
        {
            ConwayTransactionBody x => x.Mint?.Value.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value),
            BabbageTransactionBody x => x.Mint?.Value.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value),
            AlonzoTransactionBody x => x.Mint?.Value.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value),
            _ => []
        };
}