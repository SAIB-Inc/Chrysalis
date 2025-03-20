using Chrysalis.Cbor.Cardano.Extensions;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor.Utils.Transaction;
using Xunit;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Tx.Utils;
using Chrysalis.Tx.Services.Encoding;
using Chrysalis.Tx.Words;
using Chrysalis.Tx.Models.Keys;
using Chrysalis.Tx.Extensions;
using System.Text;
using Chrysalis.Tx.Models.Enums;
using Chrysalis.Tx.Models.Addresses;
using Address = Chrysalis.Tx.Models.Addresses.Address;

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

        (string hrp, byte[] data) = Bech32Codec.Decode(bech32Addr);
        string result = Convert.ToHexStringLower(data);
        // var paymentAndDelegation = Bech32Encoder.ExtractPaymentAndDelegation(data);
        // paymentAndDelegation.paymentPart
        Assert.Equal(expectedOutput, result);
    }

    [Fact]
    public void Bech32_Encode_Test()
    {
        byte[] addressBytes = Convert.FromHexString("019493315cd92eb5d8c4304e67b7e16ae36d61d34502694657811a2c8e337b62cfff6403a06a3acbc34f8c46003c69fe79a3628cefa9c47251");

        string expectedOutput = "addr1qx2fxv2umyhttkxyxp8x0dlpdt3k6cwng5pxj3jhsydzer3n0d3vllmyqwsx5wktcd8cc3sq835lu7drv2xwl2wywfgse35a3x";

        string result = Bech32Codec.Encode(addressBytes, "addr");

        Assert.Equal(expectedOutput, result);
    }

    [Fact]
    public void Mnemonic_Restore_Test()
    {
        // Generated from blaze
        byte[] expectedOutput = Convert.FromHexString("616ac02d17482feed0d68115ffee130e528aa4a4dfba6102f55e97eae40e5a09");

        string words = "gesture figure area company load wash drive south bicycle youth luggage bronze chunk false nature warrior genre bless fish cool purity already habit cement";

        Mnemonic result = Mnemonic.Restore(words, English.Words);
        Assert.Equal(expectedOutput, result.Entropy);
    }

    [Fact]
    public void Mnemonic_RestoreV2_Test()
    {
        // Generated from blaze
        byte[] expectedOutput = Convert.FromHexString("616ac02d17482feed0d68115ffee130e528aa4a4dfba6102f55e97eae40e5a09");

        string words = "gesture figure area company load wash drive south bicycle youth luggage bronze chunk false nature warrior genre bless fish cool purity already habit cement";

        Mnemonic result = Mnemonic.CreateMnemonicFromEntropy(expectedOutput, English.Words);
        Mnemonic result1 = Mnemonic.Restore(words, English.Words);

        Assert.Equal(result1.Words, result.Words);
        Assert.Equal(result1.Entropy, result.Entropy);
    }

    [Fact]
    public void Mnemonic_RootKeyHex_Test()
    {
        // Generated from blaze
        byte[] expectedKeyOutput = Convert.FromHexString("e8becc107d4081aba54a56d492d8dce48afdafbf52db0637277fbf513765db475aa54be739be6b77a827133fe7850816deed7964391048b16136206a82932b1e");
        byte[] expectedChainOutput = Convert.FromHexString("76572a2a6faf7982b945582a13fa37e0ff8a322818bdfb0f80a8c41d378398f9");

        string words = "gesture figure area company load wash drive south bicycle youth luggage bronze chunk false nature warrior genre bless fish cool purity already habit cement";

        Mnemonic mnemonic = Mnemonic.Restore(words, English.Words);
        PrivateKey rootKey = mnemonic.GetRootKey();

        Assert.Equal(expectedKeyOutput, rootKey.Key);
        Assert.Equal(expectedChainOutput, rootKey.Chaincode);
    }

    [Fact]
    public void Mnemonic_GetPublicKey_Test()
    {
        // Generated from blaze
        byte[] expectedKeyOutput = Convert.FromHexString("c2e51b9cc7a55bf75c6b48cf3fcabaaf06884e199050efb537be15a9c95d2fb1");
        byte[] expectedChainOutput = Convert.FromHexString("76572a2a6faf7982b945582a13fa37e0ff8a322818bdfb0f80a8c41d378398f9");
        string words = "gesture figure area company load wash drive south bicycle youth luggage bronze chunk false nature warrior genre bless fish cool purity already habit cement";

        Mnemonic mnemonic = Mnemonic.Restore(words, English.Words);
        PrivateKey rootKey = mnemonic.GetRootKey();
        PublicKey publicKey = rootKey.GetPublicKey();

        Assert.Equal(expectedKeyOutput, publicKey.Key);
        Assert.Equal(expectedChainOutput, publicKey.Chaincode);
    }

    [Fact]
    public void BIP32_Address_Test()
    {
        // Generated from blaze
        string expectedAddress = "addr_test1qq4sjl932yeq9jxn8eemp8u94j3v3mfma4ytnlk5kxuf4ytxs6p4768k2vdw5emdslaw29m32pxvp7ly0yfsl74crncq7evfjf";
        string words = "gesture figure area company load wash drive south bicycle youth luggage bronze chunk false nature warrior genre bless fish cool purity already habit cement";

        Mnemonic mnemonic = Mnemonic.Restore(words, English.Words);
        PrivateKey accountKey = mnemonic
            .GetRootKey()
            .Derive(PurposeType.Shelley, DerivationType.HARD)
            .Derive(CoinType.Ada, DerivationType.HARD)
            .Derive(0, DerivationType.HARD);

        PublicKey pkPub = accountKey
            .Derive(RoleType.ExternalChain)
            .Derive(0)
            .GetPublicKey();

        PublicKey skPub = accountKey
            .Derive(RoleType.Staking)
            .Derive(0)
            .GetPublicKey();

        byte[] addressBody = [.. HashUtil.Blake2b224(pkPub.Key), .. HashUtil.Blake2b224(skPub.Key)];
        byte header = AddressUtil.GetHeader(AddressUtil.GetNetworkInfo(NetworkType.Testnet), AddressType.BasePayment);
        string prefix = AddressUtil.GetPrefix(AddressType.BasePayment, NetworkType.Testnet);

        string address = Bech32Codec.Encode([header, .. addressBody], prefix);

        Assert.Equal(expectedAddress, address);
    }

    [Fact]
    public void BIP32_PublicKey_Test()
    {
        // Generated from blaze
        byte[] expectedPublicKeyBytes = Convert.FromHexString("2b097cb1513202c8d33e73b09f85aca2c8ed3bed48b9fed4b1b89a91");
        string words = "gesture figure area company load wash drive south bicycle youth luggage bronze chunk false nature warrior genre bless fish cool purity already habit cement";

        Mnemonic mnemonic = Mnemonic.Restore(words, English.Words);
        PrivateKey accountKey = mnemonic
            .GetRootKey()
            .Derive(PurposeType.Shelley, DerivationType.HARD)
            .Derive(CoinType.Ada, DerivationType.HARD)
            .Derive(0, DerivationType.HARD);

        PublicKey pkPub = accountKey
            .Derive(RoleType.ExternalChain)
            .Derive(0)
            .GetPublicKey();

        byte[] hashedPubKey = HashUtil.Blake2b224(pkPub.Key);
        Assert.Equal(expectedPublicKeyBytes, hashedPubKey);
    }

    [Fact]
    public void BIP32_StakeKey_Test()
    {
        // Generated from blaze
        byte[] expectedStakingKeyBytes = Convert.FromHexString("6686835f68f6531aea676d87fae51771504cc0fbe479130ffab81cf0");
        string words = "gesture figure area company load wash drive south bicycle youth luggage bronze chunk false nature warrior genre bless fish cool purity already habit cement";

        Mnemonic mnemonic = Mnemonic.Restore(words, English.Words);
        PrivateKey accountKey = mnemonic
            .GetRootKey()
            .Derive(PurposeType.Shelley, DerivationType.HARD)
            .Derive(CoinType.Ada, DerivationType.HARD)
            .Derive(0, DerivationType.HARD);

        PublicKey skPub = accountKey
            .Derive(RoleType.Staking)
            .Derive(0)
            .GetPublicKey();

        byte[] hashedStakeKey = HashUtil.Blake2b224(skPub.Key);

        Assert.Equal(expectedStakingKeyBytes, hashedStakeKey);
    }

    [Fact]
    public void Mnemonic_IsValidMnemonic_Test()
    {
        Mnemonic mnemonic = Mnemonic.Generate(English.Words);

        Assert.Equal([], []);
    }

    [Theory]
    [InlineData("message")]
    [InlineData("verified")]
    public void Key_SignVerify_Test(string message)
    {
        KeyPair keyPair = KeyPair.GenerateKeyPair();
        byte[] messageByte = Encoding.UTF8.GetBytes(message);

        byte[] signature = keyPair.PrivateKey.Sign(messageByte);
        bool verified = keyPair.PublicKey.Verify(messageByte, signature);

        Assert.True(verified);
    }

    [Fact]
    public void Key_SignTx_Test()
    {
        byte[] txBodyBytes = Convert.FromHexString("a300d90102818258209d18eba8c164bf6a145abe1288d206ef7bb992dfefb2c22b9eaa08162e3ac0d9020182825839002b097cb1513202c8d33e73b09f85aca2c8ed3bed48b9fed4b1b89a916686835f68f6531aea676d87fae51771504cc0fbe479130ffab81cf01a001e8480825839002b097cb1513202c8d33e73b09f85aca2c8ed3bed48b9fed4b1b89a916686835f68f6531aea676d87fae51771504cc0fbe479130ffab81cf01b00000001230c6bed021a0002917d");

        string words = "gesture figure area company load wash drive south bicycle youth luggage bronze chunk false nature warrior genre bless fish cool purity already habit cement";

        Mnemonic mnemonic = Mnemonic.Restore(words, English.Words);
        PrivateKey accountKey = mnemonic
            .GetRootKey()
            .Derive(PurposeType.Shelley, DerivationType.HARD)
            .Derive(CoinType.Ada, DerivationType.HARD)
            .Derive(0, DerivationType.HARD);

        PrivateKey pkPriv = accountKey
            .Derive(RoleType.ExternalChain)
            .Derive(0);

        PublicKey pkPub = pkPriv.GetPublicKey();

        byte[] txHash = HashUtil.Blake2b256(txBodyBytes);
        byte[] signature = pkPriv.Sign(txHash);
        byte[] signatureFromBlaze = Convert.FromHexString("846e3c4461dd98a02553c4ccbf943e015b8f6a1ec699769a6feb71087f29a4edd2d4eb3a5ba5c6b78d84bd56908e216959254cb785bec2b96e2b5a9a0f9b1809");

        bool verified = pkPub.Verify(txHash, signature);
        Assert.True(signature.SequenceEqual(signatureFromBlaze));
        Assert.True(verified);
    }

    [Fact]
    public void Address_ByteConstructor_Test()
    {
        byte[] addressBytes = Convert.FromHexString("002b097cb1513202c8d33e73b09f85aca2c8ed3bed48b9fed4b1b89a916686835f68f6531aea676d87fae51771504cc0fbe479130ffab81cf0");
        string expectedEncodedAddress = "addr_test1qq4sjl932yeq9jxn8eemp8u94j3v3mfma4ytnlk5kxuf4ytxs6p4768k2vdw5emdslaw29m32pxvp7ly0yfsl74crncq7evfjf";

        Address address = new(addressBytes);
        Address address1 = Address.FromBytes(addressBytes);
        Assert.Equal(expectedEncodedAddress, address.ToBech32());
        Assert.Equal(expectedEncodedAddress, address1.ToBech32());
    }

    [Fact]
    public void Address_Bech32Constructor_Test()
    {
        byte[] addressBytes = Convert.FromHexString("002b097cb1513202c8d33e73b09f85aca2c8ed3bed48b9fed4b1b89a916686835f68f6531aea676d87fae51771504cc0fbe479130ffab81cf0");
        string expectedEncodedAddress = "addr_test1qq4sjl932yeq9jxn8eemp8u94j3v3mfma4ytnlk5kxuf4ytxs6p4768k2vdw5emdslaw29m32pxvp7ly0yfsl74crncq7evfjf";

        Address address = new(expectedEncodedAddress);
        Address address1 = Address.FromBech32(expectedEncodedAddress);

        Assert.Equal(address.ToBytes(), addressBytes);
        Assert.Equal(address1.ToBytes(), addressBytes);
    }
}

