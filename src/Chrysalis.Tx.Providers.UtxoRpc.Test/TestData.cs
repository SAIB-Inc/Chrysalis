using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;
using Google.Protobuf;
using Utxorpc.V1Beta.Query;
using LedgerAddress = Chrysalis.Codec.Types.Cardano.Core.Common.Address;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace Chrysalis.Tx.Providers.UtxoRpcTests;

/// <summary>Constructed values for behavior tests only; golden expected values never use this helper.</summary>
internal static class TestData
{
    internal static string Hash => new('0', 64);
    internal static byte[] AddressBytes(byte marker = 1)
    {
        byte[] bytes = new byte[29]; bytes[0] = 0x60; bytes[1] = marker; return bytes;
    }
    internal static string Address(byte marker = 1) => WalletAddress.FromBytes(AddressBytes(marker)).ToBech32();
    internal static AnyUtxoData Output(uint index = 0) => new()
    {
        NativeBytes = ByteString.CopyFrom(CborSerializer.Serialize<ITransactionOutput>(AlonzoTransactionOutput.Create(
            LedgerAddress.Create(AddressBytes()), Lovelace.Create(10), null))),
        TxoRef = new TxoRef { Hash = ByteString.CopyFrom(new byte[32]), Index = index }
    };
    internal static ITransaction Transaction(bool metadata = true) => PostMaryTransaction.Create(
        AlonzoTransactionBody.Create(CborList.Create<TransactionInput>([]), CborList.Create<ITransactionOutput>([]), 0,
            null, null, null, null, null, null, null, null, null, null, null),
        PostAlonzoTransactionWitnessSet.Create(null, null, null, null, null, null, null, null), true,
        metadata ? Metadata.Create(new Dictionary<ulong, ITransactionMetadatum> { [674] = MetadataText.Create("placeholder") }) : null);
}
