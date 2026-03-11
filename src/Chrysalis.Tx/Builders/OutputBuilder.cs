using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Utils;
using Address = Chrysalis.Codec.Types.Cardano.Core.Common.Address;

namespace Chrysalis.Tx.Builders;

/// <summary>
/// Fluent builder for constructing a single transaction output.
/// Obtained from <see cref="TransactionBuilder.AddOutput(string, IValue)"/> or
/// <see cref="TransactionBuilder.AddOutput(byte[], IValue)"/>.
/// </summary>
public class OutputBuilder
{
    private readonly TransactionBuilder _parent;
    private readonly byte[] _addressBytes;
    private IValue _amount;
    private IDatumOption? _datum;
    private CborEncodedValue? _scriptRef;
    private bool _isChange;

    internal OutputBuilder(TransactionBuilder parent, byte[] addressBytes, IValue amount)
    {
        _parent = parent;
        _addressBytes = addressBytes;
        _amount = amount;
    }

    /// <summary>
    /// Attaches an inline datum to this output.
    /// </summary>
    /// <typeparam name="T">The datum type.</typeparam>
    /// <param name="datum">The datum value to serialize as inline datum.</param>
    /// <returns>This builder for chaining.</returns>
    public OutputBuilder WithInlineDatum<T>(T datum) where T : ICborType
    {
        _datum = DatumOptionExtensions.InlineDatumFrom(datum);
        return this;
    }

    /// <summary>
    /// Attaches a datum hash to this output.
    /// </summary>
    /// <param name="datumHash">The datum hash bytes.</param>
    /// <returns>This builder for chaining.</returns>
    public OutputBuilder WithDatumHash(byte[] datumHash)
    {
        _datum = DatumHashOption.Create(0, datumHash);
        return this;
    }

    /// <summary>
    /// Attaches a datum hash from a hex string to this output.
    /// </summary>
    /// <param name="datumHashHex">The hex-encoded datum hash.</param>
    /// <returns>This builder for chaining.</returns>
    public OutputBuilder WithDatumHash(string datumHashHex)
    {
        _datum = DatumHashOption.Create(0, Convert.FromHexString(datumHashHex));
        return this;
    }

    /// <summary>
    /// Attaches a script reference to this output.
    /// </summary>
    /// <param name="script">The script to attach.</param>
    /// <returns>This builder for chaining.</returns>
    public OutputBuilder WithScriptRef(IScript script)
    {
        _scriptRef = new CborEncodedValue(CborSerializer.Serialize(script));
        return this;
    }

    /// <summary>
    /// Marks this output as the change output for fee adjustment.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public OutputBuilder AsChange()
    {
        _isChange = true;
        return this;
    }

    /// <summary>
    /// Adjusts the output amount to satisfy minimum ADA requirements and adds the output.
    /// Uses the protocol parameter for cost per UTxO byte to calculate and enforce the minimum.
    /// </summary>
    /// <param name="adaPerUtxoByte">The protocol parameter for ADA cost per UTxO byte.</param>
    /// <returns>The parent <see cref="TransactionBuilder"/> for continued chaining.</returns>
    public TransactionBuilder WithMinAda(ulong adaPerUtxoByte)
    {
        ITransactionOutput output = BuildOutput();

        ulong minLovelace = FeeUtil.CalculateMinimumLovelace(adaPerUtxoByte, CborSerializer.Serialize(output));
        ulong currentLovelace = output.Amount().Lovelace();

        while (currentLovelace < minLovelace)
        {
            _amount = _amount switch
            {
                LovelaceWithMultiAsset multiAsset => LovelaceWithMultiAsset.Create(minLovelace, multiAsset.MultiAsset),
                _ => Lovelace.Create(minLovelace)
            };

            output = BuildOutput();
            minLovelace = FeeUtil.CalculateMinimumLovelace(adaPerUtxoByte, CborSerializer.Serialize(output));
            currentLovelace = output.Amount().Lovelace();
        }

        _ = _parent.AddOutput(output, _isChange);
        return _parent;
    }

    /// <summary>
    /// Builds and adds the output to the transaction without minimum ADA adjustment.
    /// </summary>
    /// <returns>The parent <see cref="TransactionBuilder"/> for continued chaining.</returns>
    public TransactionBuilder Add()
    {
        _ = _parent.AddOutput(BuildOutput(), _isChange);
        return _parent;
    }

    private ITransactionOutput BuildOutput()
    {
        Address address = new(_addressBytes);

        if (_datum is not null || _scriptRef is not null)
        {
            return PostAlonzoTransactionOutput.Create(address, _amount, _datum, _scriptRef);
        }

        return AlonzoTransactionOutput.Create(address, _amount, null);
    }
}
