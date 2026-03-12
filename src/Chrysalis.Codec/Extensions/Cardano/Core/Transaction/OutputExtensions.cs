using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core.Byron;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;

namespace Chrysalis.Codec.Extensions.Cardano.Core.Transaction;

/// <summary>
/// Extension methods for <see cref="ITransactionOutput"/> to access output fields across eras.
/// </summary>
public static class OutputExtensions
{
    /// <summary>
    /// Gets the address bytes from the transaction output.
    /// </summary>
    /// <param name="self">The transaction output instance.</param>
    /// <returns>The address bytes.</returns>
    public static ReadOnlyMemory<byte> Address(this ITransactionOutput self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            ByronTransactionOutputAdapter byron => CborSerializer.Serialize(byron.ByronTxOut.Address),
            AlonzoTransactionOutput alonzoTxOutput => alonzoTxOutput.Address.Value,
            PostAlonzoTransactionOutput postAlonzoTxOutput => postAlonzoTxOutput.Address.Value,
            _ => ReadOnlyMemory<byte>.Empty
        };
    }

    /// <summary>
    /// Gets the value from the transaction output.
    /// </summary>
    /// <param name="self">The transaction output instance.</param>
    /// <returns>The output value.</returns>
    public static IValue Amount(this ITransactionOutput self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            ByronTransactionOutputAdapter byron => Lovelace.FromAmount(byron.ByronTxOut.Amount),
            AlonzoTransactionOutput alonzoTxOutput => alonzoTxOutput.Amount,
            PostAlonzoTransactionOutput postAlonzoTxOutput => postAlonzoTxOutput.Amount,
            _ => throw new NotImplementedException()
        };
    }

    /// <summary>
    /// Gets the datum hash from the transaction output, if present.
    /// </summary>
    /// <param name="self">The transaction output instance.</param>
    /// <returns>The datum hash bytes, or null.</returns>
    public static ReadOnlyMemory<byte>? DatumHash(this ITransactionOutput self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoTransactionOutput alonzoTxOutput => alonzoTxOutput.DatumHash,
            PostAlonzoTransactionOutput postAlonzoTxOutput => postAlonzoTxOutput.Datum switch
            {
                DatumHashOption datumHashOption => datumHashOption.DatumHash,
                _ => null
            },
            _ => null
        };
    }

    /// <summary>
    /// Gets the datum option from the transaction output, if present.
    /// </summary>
    /// <param name="self">The transaction output instance.</param>
    /// <returns>The datum option, or null.</returns>
    public static IDatumOption? DatumOption(this ITransactionOutput self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            PostAlonzoTransactionOutput postAlonzoTxOutput => postAlonzoTxOutput.Datum,
            _ => null
        };
    }

    /// <summary>
    /// Gets the script reference bytes from the transaction output, if present.
    /// </summary>
    /// <param name="self">The transaction output instance.</param>
    /// <returns>The script reference bytes, or null.</returns>
    public static ReadOnlyMemory<byte>? ScriptRef(this ITransactionOutput self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            PostAlonzoTransactionOutput postAlonzoTxOutput => postAlonzoTxOutput.ScriptRef?.Value,
            _ => null
        };
    }

    /// <summary>
    /// Deserializes the inline datum from the output as <typeparamref name="T"/>.
    /// Returns default if the output has no inline datum.
    /// </summary>
    public static T? InlineDatum<T>(this ITransactionOutput self) where T : ICborType
    {
        ArgumentNullException.ThrowIfNull(self);
        if (self is PostAlonzoTransactionOutput post && post.Datum is InlineDatumOption inline)
        {
            return inline.Data.Deserialize<T>();
        }
        return default;
    }

    /// <summary>
    /// Gets the raw inline datum CBOR bytes from the output, stripping the tag-24 envelope.
    /// Returns null if the output has no inline datum.
    /// </summary>
    public static ReadOnlyMemory<byte>? InlineDatumRaw(this ITransactionOutput self)
    {
        ArgumentNullException.ThrowIfNull(self);
        if (self is PostAlonzoTransactionOutput post && post.Datum is InlineDatumOption inline)
        {
            return inline.Data.GetValue();
        }
        return null;
    }
}
