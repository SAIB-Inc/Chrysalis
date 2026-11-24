using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Fees;
using Address = Chrysalis.Codec.Types.Cardano.Core.Common.Address;

namespace Chrysalis.Tx.Builders;

/// <summary>
/// Self-contained output configuration. Address and value are required (constructor),
/// everything else is optional (fluent methods). Pass to TransactionBuilder.AddOutput.
/// </summary>
public sealed class OutputBuilder
{
    private readonly byte[] _addressBytes;
    private IDatumOption? _datum;
    private CborEncodedValue? _scriptRef;

    /// <summary>Whether this output is the change output.</summary>
    internal bool IsChange { get; set; }

    /// <summary>Sets the datum option directly (for pre-serialized datums).</summary>
    internal void SetDatumOption(IDatumOption? datum) => _datum = datum;

    /// <summary>Gets the output amount.</summary>
    internal IValue Amount { get; }

    /// <summary>Creates an output builder with a bech32 address and value.</summary>
    public OutputBuilder(string bech32Address, IValue amount)
    {
        ArgumentException.ThrowIfNullOrEmpty(bech32Address, nameof(bech32Address));
        ArgumentNullException.ThrowIfNull(amount);
        _addressBytes = Wallet.Models.Addresses.Address.FromBech32(bech32Address).ToBytes();
        Amount = amount;
    }

    /// <summary>Creates an output builder with raw address bytes and value.</summary>
    public OutputBuilder(byte[] addressBytes, IValue amount)
    {
        _addressBytes = addressBytes;
        Amount = amount;
    }

    /// <summary>Attaches an inline datum.</summary>
    public OutputBuilder WithInlineDatum<T>(T datum) where T : ICborType
    {
        _datum = DatumOptionExtensions.InlineDatumFrom(datum);
        return this;
    }

    /// <summary>Attaches a datum hash.</summary>
    public OutputBuilder WithDatumHash(byte[] datumHash)
    {
        _datum = DatumHashOption.Create(0, datumHash);
        return this;
    }

    /// <summary>Attaches a datum hash from hex.</summary>
    public OutputBuilder WithDatumHash(string datumHashHex)
    {
        _datum = DatumHashOption.Create(0, Convert.FromHexString(datumHashHex));
        return this;
    }

    /// <summary>Attaches a script reference.</summary>
    public OutputBuilder WithScriptRef(IScript script)
    {
        _scriptRef = new CborEncodedValue(CborSerializer.Serialize(script));
        return this;
    }

    /// <summary>Marks this as the change output.</summary>
    public OutputBuilder AsChange()
    {
        IsChange = true;
        return this;
    }

    /// <summary>
    /// Builds the output in the form the ledger writes it (cardano-ledger 95b369053f, Babbage/TxOut.hs:514-519): a list when it has
    /// neither an inline datum nor a reference script, else a map. With a UTxO byte cost, raises its coin to the least valid one.
    /// </summary>
    internal ITransactionOutput Build(ulong? coinsPerUtxoByte = null)
    {
        Address address = new(_addressBytes);
        ITransactionOutput built = (_datum, _scriptRef) switch
        {
            (null, null) => AlonzoTransactionOutput.Create(address, Amount, null),
            (DatumHashOption hash, null) => AlonzoTransactionOutput.Create(address, Amount, hash.DatumHash),
            _ => PostAlonzoTransactionOutput.Create(address, Amount, _datum, _scriptRef)
        };
        ulong coin = coinsPerUtxoByte is { } cost ? FeeEngine.LeastValidCoin(built, cost) : Amount.Lovelace();
        return coin == Amount.Lovelace() ? built : TransactionBuilderExtensions.ChangeCoin(built, coin);
    }

}
