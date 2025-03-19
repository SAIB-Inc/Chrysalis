using Chrysalis.Cbor.Cardano.Extensions;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor.Utils.Transaction;
using Xunit;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Tx.Utils;
using Chrysalis.Tx.Services;
using Chrysalis.Tx.Services.Encoding;
using Chrysalis.Tx.Words;
using Chrysalis.Tx.Models.Keys;
using Chrysalis.Tx.Derivations.Extensions;
using Chrysalis.Tx.Models.Enums;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Derivations;
using dotnetstandard_bip39;

namespace Chrysalis.Tx.Test;

public class TxTests
{
    private const ulong MinFeeA = 44; // lovelace per byte
    private const ulong MinFeeB = 155381; // lovelace constant fee
    private const ulong UTXO_COST_PER_BYTE = 4310;
    private const ulong MINIMUM_UTXO_LOVELACE = 840_499;

    [Fact]
    public void CalculateFee_WithSignedUnsignedCbor_ReturnsEqualFee()
    {
        // Arrange
        byte[] unsignedCborBytes = Convert.FromHexString("84a300d90102818258204b4e4749b88dfa876fbfd7a7f6e6885e0526725e396bb1b8b00806d0cac178d501018282581d6076a186d37a9219fbbf284069ca5c40432aa0e7fdb62f2efb486d111f1a02faf08082581d6076a186d37a9219fbbf284069ca5c40432aa0e7fdb62f2efb486d111f1b0000000248f53e86021a0002990da0f5f6");
        byte[] signedCborBytes = Convert.FromHexString("84a300d90102828258204b4e4749b88dfa876fbfd7a7f6e6885e0526725e396bb1b8b00806d0cac178d5018258209d18eba8c164bf6a145abe1288d206ef7bb992dfefb2c22b9eaa08162e3ac0d902018282581d6076a186d37a9219fbbf284069ca5c40432aa0e7fdb62f2efb486d111f1a02faf08082581d6076a186d37a9219fbbf284069ca5c40432aa0e7fdb62f2efb486d111f1b000000036c229788021a0002c1f5a100d9010281825820fb2bafdf7a6794c493480a3dc571b7ee90bff507e1e09bef845bbf083b6323ea58400f69eeabc179e7f865b7a08653a9401bbe0674ad346fcd85d808f2fb7203be8b2683e6a6628bfe8736b14c0e308ec07e4da6e13d62e4237a27aa40c2be1d0209f5f6");
        ulong unsigedTxSizeInBytes = (ulong)unsignedCborBytes.LongLength;
        ulong sigedTxSizeInBytes = (ulong)signedCborBytes.LongLength;

        // Act
        ulong actualFee = FeeUtil.CalculateFeeWithWitness(unsigedTxSizeInBytes, MinFeeA, MinFeeB, 1);
        ulong actualFee1 = FeeUtil.CalculateFee(sigedTxSizeInBytes, MinFeeA, MinFeeB);

        // Assert
        Assert.Equal(actualFee1, actualFee);
    }

    [Fact]
    public void CalculateFee_ValidInput_ReturnsCorrectFee()
    {
        // Arrange
        ulong txSizeInBytes = 200;
        ulong expectedFee = MinFeeA * txSizeInBytes + MinFeeB;

        // Act
        ulong actualFee = FeeUtil.CalculateFee(txSizeInBytes, MinFeeA, MinFeeB);

        // Assert
        Assert.Equal(expectedFee, actualFee);
    }

    [Fact]
    public void CalculateMinimumLovelace_AdaOnlyUtxo_ReturnsMinimumValue()
    {
        // Arrange
        byte[] cborUTxOData = Convert.FromHexString("82581d6076a186d37a9219fbbf284069ca5c40432aa0e7fdb62f2efb486d111f1a02faf080"); // Example CBOR hex

        // Calculated using cardano-cli
        ulong expectedMinAda = 849070;

        // Act
        ulong result = FeeUtil.CalculateMinimumLovelace(UTXO_COST_PER_BYTE, cborUTxOData);

        // Assert
        Assert.Equal(expectedMinAda, result);
    }

    [Fact]
    public void CalculateMinimumLovelace_UtxoWithMultiAssetAndDatum_ReturnsCalculatedMinimumAda()
    {
        // CBOR example with MultiAsset
        byte[] cborBytes = Convert.FromHexString(
            "a300581d70d27ccc13fab5b782984a3d1f99353197ca1a81be069941ffc003ee7501821a00173716a1581c8b05e87a51c1d4a0fa888d2bb14dbc25e8c343ea379a171b63aa84a0a144434e43541832028201d81843d87980"
        );

        // Calculated using cardano-cli
        ulong expectedMinAda = 1068880;

        // Act
        ulong result = FeeUtil.CalculateMinimumLovelace(UTXO_COST_PER_BYTE, cborBytes);

        // Assert
        Assert.Equal(expectedMinAda, result);
    }


    // TODO: Create test
    [Fact]
    public void RefScriptFee_Zero_Test()
    {
        // CBOR example with MultiAsset
        byte[] cborBytes = Convert.FromHexString("");

        ulong fee = FeeUtil.CalculateReferenceScriptFee(cborBytes, 15);

        // Assert
        Assert.Equal(0UL, fee);
    }

    [Fact]
    public void LargestValueFirst_AdaOnly_Test()
    {
        List<UnspentTransactionOutput> outputs = [
            TxTestUtils.CreateDummyUTxO(0, 110_000_000UL, 0),
            TxTestUtils.CreateDummyUTxO(1, 210_000_000UL, 0),
            TxTestUtils.CreateDummyUTxO(2, 110_000_000UL, 0)
        ];

        // Act
        CoinSelection result = CoinSelectionAlgorithm.LargestValueFirstAlgorithm(
            outputs,
            new Lovelace(500_000),
            new Lovelace(250_000),
            MINIMUM_UTXO_LOVELACE
        );

        UnspentTransactionOutput expectedUtxo = TxTestUtils.CreateDummyUTxO(1, 210_000_000UL, 1);
        // UnspentTransactionOutput utxo = Assert(result.SelectedUTxOs);

        Assert.Equal(expectedUtxo.TxHash, result.SelectedUTxOs[0].TxHash);
        Assert.Equal(expectedUtxo.TxIndex, result.SelectedUTxOs[0].TxIndex);
        Assert.Equal(expectedUtxo.Amount.Lovelace(), result.SelectedUTxOs[0].Amount.Lovelace());
    }

    [Fact]
    public void LargestValueFirst_WithMultiAsset_Test()
    {
        List<UnspentTransactionOutput> outputs = [
            TxTestUtils.CreateDummyUTxO(0, 110_000_000UL, 1),
            TxTestUtils.CreateDummyUTxO(1, 210_000_000UL, 2),
            TxTestUtils.CreateDummyUTxO(2, 310_000_000UL, 3)
        ];

        // Act
        CoinSelection result = CoinSelectionAlgorithm.LargestValueFirstAlgorithm(
            outputs,
            TxTestUtils.CreateDummyAssets(5_000_000UL, 3),
            TxTestUtils.CreateDummyAssets(10_000_000UL, 0),
            MINIMUM_UTXO_LOVELACE // place holder for now
        );

        UnspentTransactionOutput expectedUtxo = TxTestUtils.CreateDummyUTxO(2, 310_000_000UL, 3);
        UnspentTransactionOutput utxo = Assert.Single(result.SelectedUTxOs);

        Assert.Equal(expectedUtxo.TxHash, utxo.TxHash);
        Assert.Equal(expectedUtxo.TxIndex, utxo.TxIndex);
        Assert.Equal(expectedUtxo.Amount.Lovelace(), utxo.Amount.Lovelace());
    }

    [Fact]
    public void ScriptDataHash_EmptyParams__Test()
    {
        byte[] scriptDataHash1 = ScriptDataHashUtil.CalculateScriptDataHash(new RedeemerList([]), new([]), []);
        // Need CardanoSharp package
        // byte[] scriptDataHash2 = ScriptUtility.GenerateScriptDataHash([], [], []);

        Assert.Equal(scriptDataHash1, scriptDataHash1);
    }

    [Fact]
    public void ScriptDataHash_FromTx_Test()
    {
        // CBOR example with MultiAsset
        byte[] costModelBytes = Convert.FromHexString(
            "a1029901291a000189b41901a401011903e818ad00011903e819ea350401192baf18201a000312591920a404193e801864193e801864193e801864193e801864193e801864193e80186418641864193e8018641a000170a718201a00020782182019f016041a0001194a18b2000119568718201a0001643519030104021a00014f581a0001e143191c893903831906b419022518391a00014f580001011903e819a7a90402195fe419733a1826011a000db464196a8f0119ca3f19022e011999101903e819ecb2011a00022a4718201a000144ce1820193bc318201a0001291101193371041956540a197147184a01197147184a0119a9151902280119aecd19021d0119843c18201a00010a9618201a00011aaa1820191c4b1820191cdf1820192d1a18201a00014f581a0001e143191c893903831906b419022518391a00014f5800011a0001614219020700011a000122c118201a00014f581a0001e143191c893903831906b419022518391a00014f580001011a00014f581a0001e143191c893903831906b419022518391a00014f5800011a000e94721a0003414000021a0004213c19583c041a00163cad19fc3604194ff30104001a00022aa818201a000189b41901a401011a00013eff182019e86a1820194eae182019600c1820195108182019654d182019602f18201a0290f1e70a1a032e93af1937fd0a1a0298e40b1966c40a193e801864193e8018641a000eaf1f121a002a6e06061a0006be98011a0321aac7190eac121a00041699121a048e466e1922a4121a0327ec9a121a001e743c18241a0031410f0c1a000dbf9e011a09f2f6d31910d318241a0004578218241a096e44021967b518241a0473cee818241a13e62472011a0f23d40118481a00212c5618481a0022814619fc3b041a00032b00192076041a0013be0419702c183f00011a000f59d919aa6718fb00011a000187551902d61902cf00011a000187551902d61902cf00011a000187551902d61902cf00011a0001a5661902a800011a00017468011a00044a391949a000011a0002bfe2189f01011a00026b371922ee00011a00026e9219226d00011a0001a3e2190ce2011a00019e4919028f011a001df8bb195fc803"
        );
        byte[] redeemerBytes = Convert.FromHexString("a182000082d8799f4568656c6c6fff82194d101a005ee1c6");
        byte[] datumBytes = Convert.FromHexString("d9010281d8799f4568656c6c6fff");

        Redeemers redeemers = CborSerializer.Deserialize<Redeemers>(redeemerBytes);
        PlutusList plutusData = CborSerializer.Deserialize<PlutusList>(datumBytes);

        byte[] scriptDataHash = ScriptDataHashUtil.CalculateScriptDataHash(redeemers, plutusData, costModelBytes);

        byte[] txScriptDataHash = Convert.FromHexString("5fed45a5ef10504d92b9963a3a04beb7f6df5f4b05e30109e24f603f7d1bd974");

        Assert.Equal(scriptDataHash, txScriptDataHash);
    }

    [Fact]
    public void Bech32_Decode_Test()
    {
        string bech32Addr = "addr1qx2fxv2umyhttkxyxp8x0dlpdt3k6cwng5pxj3jhsydzer3n0d3vllmyqwsx5wktcd8cc3sq835lu7drv2xwl2wywfgse35a3x";

        string expectedOutput = "019493315cd92eb5d8c4304e67b7e16ae36d61d34502694657811a2c8e337b62cfff6403a06a3acbc34f8c46003c69fe79a3628cefa9c47251";

        var (hrp, data) = Bech32Encoder.Decode(bech32Addr);
        var result = Convert.ToHexStringLower(data);
        // var paymentAndDelegation = Bech32Encoder.ExtractPaymentAndDelegation(data);
        // paymentAndDelegation.paymentPart
        Assert.Equal(expectedOutput, result);
    }

    [Fact]
    public void Bech32_Encode_Test()
    {
        byte[] addressBytes = Convert.FromHexString("019493315cd92eb5d8c4304e67b7e16ae36d61d34502694657811a2c8e337b62cfff6403a06a3acbc34f8c46003c69fe79a3628cefa9c47251");

        string expectedOutput = "addr1qx2fxv2umyhttkxyxp8x0dlpdt3k6cwng5pxj3jhsydzer3n0d3vllmyqwsx5wktcd8cc3sq835lu7drv2xwl2wywfgse35a3x";

        string result = Bech32Encoder.Encode(addressBytes, "addr");
        

        Assert.Equal(expectedOutput, result);
    }

    [Fact]
    public void AddressDerivation_Encode_Test()
    {
        string words = "gesture figure area company load wash drive south bicycle youth luggage bronze chunk false nature warrior genre bless fish cool purity already habit cement";

        MnemonicKey mnemonic = Mnemonic.Restore(words, English.Words);
        PrivateKey rootKey = mnemonic.GetRootKey();

        (PrivateKey? paymentPrv1, PublicKey? paymentPub1) = TxTestUtils.GetKeyPairFromPath("m/1855'/1815'/0'", rootKey);

        string bip39words = "gesture\r figure\r area\r company\r load\r wash\r drive\r south\r bicycle\r youth\r luggage\r bronze\r chunk\r false\r nature\r warrior\r genre\r bless\r fish\r cool\r purity\r already\r habit\r cement\r";
        var bip39 = new BIP39();
        var bip39words1 = bip39.GenerateMnemonic(256, BIP39Wordlist.English);
        byte[] entropy = Convert.FromHexString(bip39.MnemonicToEntropy(bip39words, BIP39Wordlist.English));
        MnemonicKey mnemonicKey = new(words.Split(' '), entropy);

        Assert.Equal(mnemonicKey.GetRootKey().Key, mnemonic.GetRootKey().Key);
        Assert.Equal(mnemonicKey.GetRootKey().Chaincode, mnemonic.GetRootKey().Chaincode);

        // IAccountNodeDerivation paymentDerivation = rootKey.Derive()     // IMasterNodeDerivation
        //     .Derive(PurposeType.PolicyKeys)             // IPurposeNodeDerivation
        //     .Derive(CoinType.Ada)                       // ICoinNodeDerivation
        //     .Derive(0);                                 // IAccountNodeDerivation

        // Assert
        // Assert.Equal(paymentPrv1.Key, paymentDerivation.PrivateKey.Key);
        // Assert.Equal(paymentPrv1.Chaincode, paymentDerivation.PrivateKey.Chaincode);

        // Assert.Equal(paymentPub1.Key, paymentDerivation.PublicKey.Key);
        // Assert.Equal(paymentPub1.Chaincode, paymentDerivation.PublicKey.Chaincode);
    }
}

