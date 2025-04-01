using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Providers;
using Chrysalis.Tx.Utils;
using Chrysalis.Wallet.Models.Addresses;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Utils;
using Chrysalis.Wallet.Words;
using Address = Chrysalis.Cbor.Types.Cardano.Core.Common.Address;

namespace Chrysalis.Tx.Cli;

public static class SampleTransactions
{

    public static async Task<byte[]> SendLovelaceAsync()
    {
        var rico = "005c5c318d01f729e205c95eb1b02d623dd10e78ea58f72d0c13f892b2e8904edc699e2f0ce7b72be7cec991df651a222e2ae9244eb5975cba";
        var provider = new Blockfrost("previewajMhMPYerz9Pd3GsqjayLwP5mgnNnZCC");

        byte[] expectedOutput = Convert.FromHexString("616ac02d17482feed0d68115ffee130e528aa4a4dfba6102f55e97eae40e5a09");

        string words = "mosquito creek harbor detail change secret design mistake next labor bench mule elite vapor menu hurdle what tobacco improve caught anger aware legal project";

        Mnemonic mnemonic = Mnemonic.Restore(words, English.Words);

        PrivateKey accountKey = mnemonic
                    .GetRootKey()
                    .Derive(PurposeType.Shelley, DerivationType.HARD)
                    .Derive(CoinType.Ada, DerivationType.HARD)
                    .Derive(0, DerivationType.HARD);

        PrivateKey privateKey = accountKey
                    .Derive(RoleType.ExternalChain)
                    .Derive(0);

        PublicKey pkPub = privateKey.GetPublicKey();

        PublicKey skPub = accountKey
                    .Derive(RoleType.Staking)
                    .Derive(0)
                    .GetPublicKey();

        byte[] addressBody = [.. HashUtil.Blake2b224(pkPub.Key), .. HashUtil.Blake2b224(skPub.Key)];
        AddressHeader header = new(AddressType.BasePayment, NetworkType.Testnet);
        string address = Bech32Util.Encode([header.ToByte(), .. addressBody], header.GetPrefix());

        var utxos = await provider.GetUtxosAsync(address);
        var pparams = await provider.GetParametersAsync();
        var txBuilder = TransactionBuilder.Create(pparams);


        var output = new PostAlonzoTransactionOutput(
            new Address(Convert.FromHexString(rico)),
            new Lovelace(10000000UL),
            null,
            null
        );

        ResolvedInput? feeInput = null;
        foreach (var utxo in utxos.OrderByDescending(e =>
        {
            ulong amount = e.Output switch
            {
                AlonzoTransactionOutput alonzoTransactionOutput => alonzoTransactionOutput.Amount switch
                {
                    Lovelace value => value.Value,
                    LovelaceWithMultiAsset multiAsset => multiAsset.LovelaceValue.Value,
                    _ => 0
                },
                PostAlonzoTransactionOutput postAlonzoTransactionOutput => postAlonzoTransactionOutput.Amount switch
                {
                    Lovelace value => value.Value,
                    LovelaceWithMultiAsset multiAsset => multiAsset.LovelaceValue.Value,
                    _ => 0
                },
                _ => 0
            };
            return amount;
        }))
        {
            ulong lovelaceValue = utxo.Output switch
            {
                AlonzoTransactionOutput alonzoTransactionOutput => alonzoTransactionOutput.Amount switch
                {
                    Lovelace value => value.Value,
                    LovelaceWithMultiAsset multiAsset => multiAsset.LovelaceValue.Value,
                    _ => 0
                },
                PostAlonzoTransactionOutput postAlonzoTransactionOutput => postAlonzoTransactionOutput.Amount switch
                {
                    Lovelace value => value.Value,
                    LovelaceWithMultiAsset multiAsset => multiAsset.LovelaceValue.Value,
                    _ => 0
                },
                _ => 0
            };

            Value utxoValue = utxo.Output switch
            {
                AlonzoTransactionOutput alonzoTransactionOutput => alonzoTransactionOutput.Amount,
                PostAlonzoTransactionOutput postAlonzoTransactionOutput => postAlonzoTransactionOutput.Amount,
                _ => throw new Exception("Invalid change output type")
            };
            if (lovelaceValue >= 5000000UL && utxoValue is Lovelace)
            {
                feeInput = utxo;
                break;
            }
        }

        if (feeInput is not null)
        {
            utxos.Remove(feeInput);
            txBuilder.AddInput(feeInput.Outref);
        }

        var coinSelectionResult = CoinSelectionUtil.LargestFirstAlgorithm(utxos, [output.Amount]);

        foreach (var consumed_input in coinSelectionResult.Inputs)
        {
            txBuilder.AddInput(consumed_input.Outref);
        }

        ulong feeLovelace = feeInput?.Output switch
        {
            AlonzoTransactionOutput alonzoTransactionOutput => alonzoTransactionOutput.Amount switch
            {
                Lovelace value => value.Value,
                LovelaceWithMultiAsset multiAsset => multiAsset.LovelaceValue.Value,
                _ => 0
            },
            PostAlonzoTransactionOutput postAlonzoTransactionOutput => postAlonzoTransactionOutput.Amount switch
            {
                Lovelace value => value.Value,
                LovelaceWithMultiAsset multiAsset => multiAsset.LovelaceValue.Value,
                _ => 0
            },
            _ => 0
        };

        var lovelaceChange = new Lovelace(coinSelectionResult.LovelaceChange + feeLovelace);
        Value changeValue = lovelaceChange;

        if (coinSelectionResult.AssetsChange.Count > 0)
        {
            changeValue = new LovelaceWithMultiAsset(lovelaceChange, new MultiAssetOutput(coinSelectionResult.AssetsChange));
        }

        var changeOutput = new PostAlonzoTransactionOutput(
            new Address(Convert.FromHexString(rico)),
            changeValue,
            null,
            null
        );

        txBuilder
            .AddOutput(output)
            .AddOutput(changeOutput, true)
            .CalculateFee();

        var unsignedTx = txBuilder.Build();
        Transaction signedTx = unsignedTx.Sign(privateKey);


        return CborSerializer.Serialize(signedTx);
    }

    public static async Task<byte[]> LockLovelaceAsync()
    {
        var rico = "005c5c318d01f729e205c95eb1b02d623dd10e78ea58f72d0c13f892b2e8904edc699e2f0ce7b72be7cec991df651a222e2ae9244eb5975cba";
        var provider = new Blockfrost("previewajMhMPYerz9Pd3GsqjayLwP5mgnNnZCC");

        byte[] expectedOutput = Convert.FromHexString("616ac02d17482feed0d68115ffee130e528aa4a4dfba6102f55e97eae40e5a09");

        string words = "mosquito creek harbor detail change secret design mistake next labor bench mule elite vapor menu hurdle what tobacco improve caught anger aware legal project";

        Mnemonic mnemonic = Mnemonic.Restore(words, English.Words);

        PrivateKey accountKey = mnemonic
                    .GetRootKey()
                    .Derive(PurposeType.Shelley, DerivationType.HARD)
                    .Derive(CoinType.Ada, DerivationType.HARD)
                    .Derive(0, DerivationType.HARD);

        PrivateKey privateKey = accountKey
                    .Derive(RoleType.ExternalChain)
                    .Derive(0);

        PublicKey pkPub = privateKey.GetPublicKey();

        PublicKey skPub = accountKey
                    .Derive(RoleType.Staking)
                    .Derive(0)
                    .GetPublicKey();

        byte[] addressBody = [.. HashUtil.Blake2b224(pkPub.Key), .. HashUtil.Blake2b224(skPub.Key)];
        AddressHeader header = new(AddressType.BasePayment, NetworkType.Testnet);
        string address = Bech32Util.Encode([header.ToByte(), .. addressBody], header.GetPrefix());


        var scriptAddress = "70d27ccc13fab5b782984a3d1f99353197ca1a81be069941ffc003ee75";

        var utxos = await provider.GetUtxosAsync(address);
        var pparams = await provider.GetParametersAsync();
        var txBuilder = TransactionBuilder.Create(pparams);


        var output = new PostAlonzoTransactionOutput(
            new Address(Convert.FromHexString(scriptAddress)),
            new Lovelace(10000000UL),
            new InlineDatumOption(1, new CborEncodedValue(Convert.FromHexString("d87980"))),
            null
        );

        ResolvedInput? feeInput = null;
        foreach (var utxo in utxos.OrderByDescending(e => e.Output.Amount()!.Lovelace()))
        {
            if (utxo.Output.Amount()!.Lovelace() >= 5000000UL && utxo.Output.Amount() is Lovelace)
            {
                feeInput = utxo;
                break;
            }
        }

        if (feeInput is not null)
        {
            utxos.Remove(feeInput);
            txBuilder.AddInput(feeInput.Outref);
        }

        var coinSelectionResult = CoinSelectionUtil.LargestFirstAlgorithm(utxos, [output.Amount]);

        foreach (var consumed_input in coinSelectionResult.Inputs)
        {
            txBuilder.AddInput(consumed_input.Outref);
        }

        var lovelaceChange = new Lovelace(coinSelectionResult.LovelaceChange + feeInput?.Output.Amount()!.Lovelace() ?? 0);
        Value changeValue = lovelaceChange;

        if (coinSelectionResult.AssetsChange.Count > 0)
        {
            changeValue = new LovelaceWithMultiAsset(lovelaceChange, new MultiAssetOutput(coinSelectionResult.AssetsChange));
        }

        var changeOutput = new PostAlonzoTransactionOutput(
            new Address(Convert.FromHexString(rico)),
            changeValue,
            null,
            null
        );

        txBuilder
            .AddOutput(output)
            .AddOutput(changeOutput, true)
            .CalculateFee();

        var unsignedTx = txBuilder.Build();
        Transaction signedTx = unsignedTx.Sign(privateKey);


        return CborSerializer.Serialize(signedTx);
    }

    public static async Task<byte[]> UnlockLovelaceAsync()
    {

        var rico = "005c5c318d01f729e205c95eb1b02d623dd10e78ea58f72d0c13f892b2e8904edc699e2f0ce7b72be7cec991df651a222e2ae9244eb5975cba";
        // var bob = "005c5c318d01f729e205c95eb1b02d623dd10e78ea58f72d0c13f892b2e8904edc699e2f0ce7b72be7cec991df651a222e2ae9244eb5975cba";

        string scriptBytes = "585e585c01010029800aba2aba1aab9eaab9dab9a4888896600264653001300600198031803800cc0180092225980099b8748008c01cdd500144c8cc892898050009805180580098041baa0028b200c180300098019baa0068a4d13656400401";
        var provider = new Blockfrost("previewajMhMPYerz9Pd3GsqjayLwP5mgnNnZCC");

        byte[] expectedOutput = Convert.FromHexString("616ac02d17482feed0d68115ffee130e528aa4a4dfba6102f55e97eae40e5a09");

        string words = "mosquito creek harbor detail change secret design mistake next labor bench mule elite vapor menu hurdle what tobacco improve caught anger aware legal project";

        Mnemonic mnemonic = Mnemonic.Restore(words, English.Words);

        PrivateKey accountKey = mnemonic
                    .GetRootKey()
                    .Derive(PurposeType.Shelley, DerivationType.HARD)
                    .Derive(CoinType.Ada, DerivationType.HARD)
                    .Derive(0, DerivationType.HARD);

        PrivateKey privateKey = accountKey
                    .Derive(RoleType.ExternalChain)
                    .Derive(0);

        PublicKey pkPub = privateKey.GetPublicKey();

        PublicKey skPub = accountKey
                    .Derive(RoleType.Staking)
                    .Derive(0)
                    .GetPublicKey();

        byte[] addressBody = [.. HashUtil.Blake2b224(pkPub.Key), .. HashUtil.Blake2b224(skPub.Key)];
        AddressHeader header = new(AddressType.BasePayment, NetworkType.Testnet);
        string address = Bech32Util.Encode([header.ToByte(), .. addressBody], header.GetPrefix());

        string scriptRefTxHash = "6ba7ea1e216dfc0d47ae9d5ba2045acffdc52c592a0f1efd1ad5dedf4bdc8cea";

        var utxos = await provider.GetUtxosAsync(address);
        var utxos_copy = new List<ResolvedInput>(utxos);
        var pparams = await provider.GetParametersAsync();

        var txBuilder = TransactionBuilder.Create(pparams);

        var scriptAddress = "70d27ccc13fab5b782984a3d1f99353197ca1a81be069941ffc003ee75";
        var lockedUtxoTxHash = "d33e699974257d4df9698f5eb3d1bfd2245e0d8f9c24db37d04c1e0ed478c44a";

        // var policy = "0401ce1fb4da93ce46ed4a22c95d3502a59c76476649a41e7bafa9da";
        // var assetName = "494147";
        // Dictionary<CborBytes, CborUlong> tokenBundle = new(){
        //     { new CborBytes(Convert.FromHexString(assetName)), new CborUlong(1000) }
        // };

        // Dictionary<CborBytes, TokenBundleOutput> multiAsset = new(){
        //     { new CborBytes(Convert.FromHexString(policy)), new TokenBundleOutput(tokenBundle) }
        // };

        // var output = new AlonzoTransactionOutput(
        //     new Address(Convert.FromHexString(rico)),
        //     new LovelaceWithMultiAsset(new Lovelace(2000000UL), new MultiAssetOutput(multiAsset)),
        //     null
        //     );

        byte[] cborEncodedScript = Convert.FromHexString("8203585E585C01010029800ABA2ABA1AAB9EAAB9DAB9A4888896600264653001300600198031803800CC0180092225980099B8748008C01CDD500144C8CC892898050009805180580098041BAA0028B200C180300098019BAA0068A4D13656400401");

        var refInput = new TransactionInput(Convert.FromHexString(scriptRefTxHash), 0);
        var refOutput = new PostAlonzoTransactionOutput(
            new Address(Convert.FromHexString(scriptAddress)),
            new Lovelace(1301620UL),
            null,
            new CborEncodedValue(cborEncodedScript)
            );
        var refUtxo = new ResolvedInput(refInput, refOutput);

        var lockedUtxoOutref = new TransactionInput(Convert.FromHexString(lockedUtxoTxHash), 0);

        var datum = new InlineDatumOption(1, new CborEncodedValue(Convert.FromHexString("d87980")));

        //Todo Implement GetResolvedUtxo in provider
        var lockedUtxo = new ResolvedInput(
            lockedUtxoOutref,
            new PostAlonzoTransactionOutput(
                new Address(Convert.FromHexString(scriptAddress)),
                 new Lovelace(20000000UL), datum, null));

        utxos_copy.Add(lockedUtxo);
        utxos_copy.Add(refUtxo);

        var output = new PostAlonzoTransactionOutput(
            new Address(Convert.FromHexString(scriptAddress)),
            new Lovelace(10000000UL),
            null,
            null
            );


        ResolvedInput? feeInput = null;
        foreach (var utxo in utxos.OrderByDescending(e => e.Output.Amount()!.Lovelace()))
        {
            if (utxo.Output.Amount()!.Lovelace() >= 5000000UL && utxo.Output.Amount() is Lovelace)
            {
                feeInput = utxo;
                break;
            }
        }

        if (feeInput is not null)
        {
            utxos.Remove(feeInput);
            txBuilder.AddInput(feeInput.Outref);
        }


        ResolvedInput? collateralInput = null;
        foreach (var utxo in utxos.OrderByDescending(e => e.Output.Amount()!.Lovelace()))
        {
            if (utxo.Output.Amount()!.Lovelace() >= 5000000UL && utxo.Output.Amount() is Lovelace)
            {
                collateralInput = utxo;
                break;
            }
        }

        if (collateralInput is not null)
        {
            utxos.Remove(collateralInput);
            txBuilder.AddCollateral(collateralInput.Outref);
        }


        // var coinSelectionResult = CoinSelectionAlgorithm.LargestFirstAlgorithm(utxos, [output.Amount]);

        // foreach (var consumed_input in coinSelectionResult.Inputs)
        // {
        //     txBuilder.AddInput(consumed_input.Outref);
        // }


        var lovelaceChange = new Lovelace(feeInput?.Output.Amount()!.Lovelace() ?? 0);
        Value changeValue = lovelaceChange;

        // if (coinSelectionResult.AssetsChange.Count > 0)
        // {
        //     changeValue = new LovelaceWithMultiAsset(lovelaceChange, new MultiAssetOutput(coinSelectionResult.AssetsChange));
        // }


        var changeOutput = new PostAlonzoTransactionOutput(
            new Address(Convert.FromHexString(rico)),
            changeValue,
            null,
            null
            );

        var collateralOutput = new PostAlonzoTransactionOutput(
            new Address(Convert.FromHexString(rico)),
            new Lovelace(collateralInput?.Output.Amount()!.Lovelace() ?? 0),
            null,
            null
            );

        var redeemerKey = new RedeemerKey(0, 0);
        var redeemerValue = new RedeemerValue(new PlutusConstr([]), new ExUnits(pparams.MaxTxExUnits!.Mem, pparams.MaxTxExUnits!.Steps));

        var redeemers = new RedeemerMap(new Dictionary<RedeemerKey, RedeemerValue> { { redeemerKey, redeemerValue } });
        // var redeemer = new RedeemerEntry(new CborInt(0), new CborUlong(0), CborSerializer.Deserialize<PlutusData>(Convert.FromHexString("d87980")), new ExUnits(new CborUlong(0), new CborUlong(0)));

        txBuilder
            .AddReferenceInput(refInput)
            .AddInput(lockedUtxoOutref)
            .AddOutput(output)
            .AddOutput(changeOutput, true)
            .SetCollateralReturn(collateralOutput)
            .SetRedeemers(redeemers)
            .Evaluate(utxos_copy)
            .CalculateFee(Convert.FromHexString(scriptBytes));


        var unsignedTx = txBuilder.Build();

        Transaction signedTx = unsignedTx
            .Sign(privateKey);

        var signedTxHash = CborSerializer.Serialize(signedTx);

        return signedTxHash;
    }

}