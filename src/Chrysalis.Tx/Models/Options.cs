using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Tx.Builders;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Tx.Utils;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;
using Address = Chrysalis.Codec.Types.Cardano.Core.Common.Address;


namespace Chrysalis.Tx.Models;

/// <summary>
/// Configuration options for transaction inputs.
/// </summary>
/// <typeparam name="T">The transaction parameter type.</typeparam>
public record InputOptions<T>
{
    /// <summary>Gets or sets the party identifier for the input address.</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>Gets or sets the minimum required value for the input.</summary>
    public IValue? MinAmount { get; set; }

    /// <summary>Gets or sets a specific UTxO reference to consume.</summary>
    public TransactionInput? UtxoRef { get; set; }

    /// <summary>Gets or sets the datum option for script inputs.</summary>
    public IDatumOption? Datum { get; set; }

    /// <summary>Gets or sets the identifier for this input configuration.</summary>
    public string? Id { get; set; }

    /// <summary>Gets or sets the redeemer map for script validation.</summary>
    public RedeemerMap? Redeemer { get; set; }

    /// <summary>Gets or sets the redeemer builder function.</summary>
    public Func<InputOutputMapping, T, TransactionBuilder, Redeemer<ICborType>>? RedeemerBuilder { get; set; }

    /// <summary>
    /// Sets a typed redeemer builder for this input.
    /// </summary>
    /// <typeparam name="TData">The redeemer data type.</typeparam>
    /// <param name="factory">The factory function that builds redeemer data.</param>
    /// <param name="tag">The redeemer tag (defaults to Spend).</param>
    /// <returns>This options instance for chaining.</returns>
    public InputOptions<T> SetRedeemerBuilder<TData>(RedeemerDataBuilder<T, TData> factory, RedeemerTag tag = RedeemerTag.Spend)
        where TData : ICborType
    {
        ArgumentNullException.ThrowIfNull(factory);

        RedeemerBuilder = (mapping, context, transactionBuilder) =>
        {
            TData data = factory(mapping, context, transactionBuilder);
            return new Redeemer<ICborType>(tag, 0, data, ExUnits.Create(157374, 49443675));
        };
        return this;
    }
}

/// <summary>
/// Configuration options for reference inputs.
/// </summary>
public record ReferenceInputOptions
{
    /// <summary>Gets or sets the party identifier for the reference input address.</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>Gets or sets a specific UTxO reference.</summary>
    public TransactionInput? UtxoRef { get; set; }

    /// <summary>Gets or sets the identifier for this reference input.</summary>
    public string? Id { get; set; }
}


/// <summary>
/// Configuration options for transaction outputs.
/// </summary>
public record OutputOptions
{
    /// <summary>Gets or sets the party identifier for the output address.</summary>
    public string To { get; set; } = string.Empty;

    /// <summary>Gets or sets the output value.</summary>
    public IValue? Amount { get; set; }

    /// <summary>Gets or sets the datum option for the output.</summary>
    public IDatumOption? Datum { get; set; }

    /// <summary>Gets or sets the associated input identifier for mapping.</summary>
    public string? AssociatedInputId { get; set; }

    /// <summary>Gets or sets the identifier for this output.</summary>
    public string? Id { get; set; }

    /// <summary>Gets or sets the script to attach to the output.</summary>
    public IScript? IScript { get; set; }

    /// <summary>
    /// Sets an inline datum on this output from a typed CBOR value.
    /// </summary>
    /// <typeparam name="TDatum">The datum type.</typeparam>
    /// <param name="datum">The datum value to serialize.</param>
    public void SetDatum<TDatum>(TDatum datum)
        where TDatum : ICborType
    {
        ArgumentNullException.ThrowIfNull(datum);
        byte[] plutusBytes = CborSerializer.Serialize(
            CborSerializer.Deserialize<IPlutusData>(CborSerializer.Serialize(datum)));
        Datum = InlineDatumOption.Create(1, CborEncodedValue.WrapTag24(plutusBytes));
    }

    /// <summary>
    /// Builds a transaction output from these options.
    /// </summary>
    /// <param name="parties">The party identifier to address mapping.</param>
    /// <param name="adaPerUtxoByte">The ADA per UTxO byte protocol parameter.</param>
    /// <returns>The constructed transaction output.</returns>
    public ITransactionOutput BuildOutput(Dictionary<string, string> parties, ulong adaPerUtxoByte)
    {
        ArgumentNullException.ThrowIfNull(parties);

        Address address = new(WalletAddress.FromBech32(parties[To]).ToBytes());
        CborEncodedValue? script = IScript is not null ? new CborEncodedValue(CborSerializer.Serialize(IScript)) : null;
        ITransactionOutput output = AlonzoTransactionOutput.Create(address, Amount ?? Lovelace.Create(1000000), null);

        if (Datum is not null || IScript is not null)
        {
            output = PostAlonzoTransactionOutput.Create(address, Amount ?? Lovelace.Create(1000000), Datum, script);
        }

        ulong minLovelace = FeeUtil.CalculateMinimumLovelace(adaPerUtxoByte, CborSerializer.Serialize(output));
        ulong currentLovelace = output.Amount().Lovelace();

        while (currentLovelace < minLovelace)
        {
            IValue amount = output.Amount();
            amount = amount switch
            {
                LovelaceWithMultiAsset multiAsset => LovelaceWithMultiAsset.Create(minLovelace, multiAsset.MultiAsset),
                _ => Lovelace.Create(minLovelace)
            };

            output = output switch
            {
                AlonzoTransactionOutput alonzoOutput => AlonzoTransactionOutput.Create(alonzoOutput.Address, amount, alonzoOutput.DatumHash),
                PostAlonzoTransactionOutput postAlonzoOutput => PostAlonzoTransactionOutput.Create(postAlonzoOutput.Address, amount, postAlonzoOutput.Datum, postAlonzoOutput.ScriptRef),
                _ => throw new InvalidOperationException("Unsupported transaction output type")
            };

            minLovelace = FeeUtil.CalculateMinimumLovelace(adaPerUtxoByte, CborSerializer.Serialize(output));
            currentLovelace = output.Amount().Lovelace();
        }

        return output;
    }
}

/// <summary>
/// Configuration options for minting operations.
/// </summary>
/// <typeparam name="T">The transaction parameter type.</typeparam>
public record MintOptions<T>
{
    /// <summary>Gets or sets the minting policy hex string.</summary>
    public string Policy { get; set; } = string.Empty;

    /// <summary>Gets or sets the assets to mint (name to quantity).</summary>
    public Dictionary<string, long> Assets { get; init; } = [];

    /// <summary>Gets or sets the redeemer map for script validation.</summary>
    public RedeemerMap? Redeemer { get; set; }

    /// <summary>Gets or sets the redeemer builder function.</summary>
    public Func<InputOutputMapping, T, TransactionBuilder, Redeemer<ICborType>>? RedeemerBuilder { get; set; }

    /// <summary>
    /// Sets a typed redeemer builder for this mint operation.
    /// </summary>
    /// <typeparam name="TData">The redeemer data type.</typeparam>
    /// <param name="factory">The factory function that builds redeemer data.</param>
    /// <returns>This options instance for chaining.</returns>
    public MintOptions<T> SetRedeemerBuilder<TData>(RedeemerDataBuilder<T, TData> factory)
        where TData : ICborType
    {
        ArgumentNullException.ThrowIfNull(factory);

        RedeemerBuilder = (mapping, context, transactionBuilder) =>
        {
            TData data = factory(mapping, context, transactionBuilder);
            return new Redeemer<ICborType>(RedeemerTag.Mint, 0, data, ExUnits.Create(98397, 25938682));
        };
        return this;
    }

    /// <summary>Gets or sets the identifier for this mint configuration.</summary>
    public string? Id { get; set; }
}

/// <summary>
/// Configuration options for withdrawal operations.
/// </summary>
/// <typeparam name="T">The transaction parameter type.</typeparam>
public record WithdrawalOptions<T>
{
    /// <summary>Gets or sets the party identifier for the withdrawal address.</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>Gets or sets the withdrawal amount.</summary>
    public ulong Amount { get; set; }

    /// <summary>Gets or sets the identifier for this withdrawal.</summary>
    public string? Id { get; set; }

    /// <summary>Gets or sets the redeemer map for script validation.</summary>
    public RedeemerMap? Redeemer { get; set; }

    /// <summary>Gets or sets the redeemer builder function.</summary>
    public Func<InputOutputMapping, T, TransactionBuilder, Redeemer<ICborType>>? RedeemerBuilder { get; set; }

    /// <summary>
    /// Sets a typed redeemer builder for this withdrawal.
    /// </summary>
    /// <typeparam name="TData">The redeemer data type.</typeparam>
    /// <param name="factory">The factory function that builds redeemer data.</param>
    /// <returns>This options instance for chaining.</returns>
    public WithdrawalOptions<T> SetRedeemerBuilder<TData>(RedeemerDataBuilder<T, TData> factory)
        where TData : ICborType
    {
        ArgumentNullException.ThrowIfNull(factory);

        RedeemerBuilder = (mapping, context, transactionBuilder) =>
        {
            TData data = factory(mapping, context, transactionBuilder);
            return new Redeemer<ICborType>(RedeemerTag.Reward, 0, data, ExUnits.Create(1648071, 497378507));
        };
        return this;
    }
}
