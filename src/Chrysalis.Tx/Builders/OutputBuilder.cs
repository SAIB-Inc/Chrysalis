using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
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
    internal void SetDatumOption(IDatumOption datum) => _datum = datum;

    /// <summary>Gets or sets the output amount.</summary>
    internal IValue Amount { get; set; }

    /// <summary>Creates an output builder with a bech32 address and value.</summary>
    public OutputBuilder(string bech32Address, IValue amount)
    {
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

    /// <summary>Builds the transaction output. Called internally by TransactionBuilder.</summary>
    internal ITransactionOutput Build()
    {
        Address address = new(_addressBytes);
        return PostAlonzoTransactionOutput.Create(address, Amount, _datum, _scriptRef);
    }

}
