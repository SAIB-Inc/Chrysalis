using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Tx.Models.Cbor;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Tx.Fees;

/// <summary>Exact fee and collateral rules from Alonzo onward, and minimum-output rules from Babbage onward.</summary>
public static class FeeEngine
{
    /// <summary>Computes the linear fee in lovelace, given the ledger size in bytes.</summary>
    public static ulong LinearFee(ulong ledgerSize, ProtocolParams pp)
    {
        ArgumentNullException.ThrowIfNull(pp);
        return checked((ulong)ExactFee.Linear(ledgerSize, Parameter(pp.MinFeeA, nameof(pp.MinFeeA)), Parameter(pp.MinFeeB, nameof(pp.MinFeeB))));
    }

    /// <summary>Computes the fee for the sum of the redeemers' execution units, rounded up once.</summary>
    public static ulong ScriptFee(IRedeemers? redeemers, ProtocolParams pp)
    {
        ArgumentNullException.ThrowIfNull(pp);
        return checked((ulong)ScriptFeeWide(redeemers, pp));
    }

    internal static UInt128 ScriptFeeWide(IRedeemers? redeemers, ProtocolParams pp)
    {
        if (redeemers is null)
        {
            return 0;
        }
        UInt128 mem = 0;
        UInt128 steps = 0;
        switch (redeemers)
        {
            case RedeemerList list:
                foreach (RedeemerEntry entry in list.Value)
                {
                    mem += entry.ExUnits.Mem;
                    steps += entry.ExUnits.Steps;
                }
                break;
            case RedeemerMap map:
                foreach ((_, RedeemerValue value) in map.Value)
                {
                    mem += value.ExUnits.Mem;
                    steps += value.ExUnits.Steps;
                }
                break;
            default: throw new ArgumentException("Unknown redeemer form.", nameof(redeemers));
        }
        ExUnitPrices price = Parameter(pp.ExecutionCosts, nameof(pp.ExecutionCosts));
        return ExactFee.Script(mem, steps, price.MemPrice, price.StepPrice);
    }

    /// <summary>Computes the tiered reference-script fee, rounded down once. Sizes over 204,800 bytes are rejected.</summary>
    public static ulong ReferenceScriptFee(ulong size, ProtocolParams pp)
    {
        ArgumentNullException.ThrowIfNull(pp);
        return checked((ulong)ReferenceScriptFeeWide(size, pp));
    }

    internal static UInt128 ReferenceScriptFeeWide(ulong size, ProtocolParams pp)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, ExactFee.MaxReferenceScriptSize);
        // Babbage ignores reference-script size; Conway requires an explicit price, even if zero.
        return size == 0 || pp.ProtocolVersion is { Major: < 9 } ? 0
            : ExactFee.Reference(size, pp.MinFeeRefScriptCostPerByte ?? throw new InvalidDataException("Missing protocol parameter MinFeeRefScriptCostPerByte."));
    }

    /// <summary>Counts scripts on the distinct spending and reference outrefs. Every outref must be resolved.</summary>
    public static ulong ReferenceScriptSize(IEnumerable<TransactionInput> inputs, IEnumerable<TransactionInput> referenceInputs,
        IReadOnlyCollection<ResolvedInput> utxos)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(referenceInputs);
        ArgumentNullException.ThrowIfNull(utxos);
        Dictionary<TransactionInput, ResolvedInput> resolved = new(utxos.Count, OutrefComparer.Instance);
        foreach (ResolvedInput item in utxos)
        {
            _ = resolved.TryAdd(item.Outref, item);
        }
        ulong size = 0;
        foreach (TransactionInput input in inputs.Union(referenceInputs, OutrefComparer.Instance))
        {
            if (!resolved.TryGetValue(input, out ResolvedInput? item))
            {
                throw new InvalidOperationException($"Unresolved input {Convert.ToHexStringLower(input.TransactionId.Span)}#{input.Index}.");
            }
            if (item.Output.ScriptRef() is { } script)
            {
                size = checked(size + ScriptSize(script.Span));
            }
        }
        return size;
    }

    private static ulong ScriptSize(ReadOnlySpan<byte> bytes)
    {
        CborReader reader = new(bytes);
        reader.ReadBeginArray();
        int elements = reader.ReadSize();
        ulong language = reader.ReadUInt64();
        if (elements is not (2 or -1) || language > 3)
        {
            throw new InvalidDataException("Invalid Conway reference script.");
        }
        ulong size;
        if (language != 0)
        {
            // Plutus charges the byte string's content, not its framing or the language tag.
            size = (ulong)reader.ReadByteString().Length;
        }
        else
        {
            int before = reader.Buffer.Length;
            reader.SkipDataItem();
            size = (ulong)(before - reader.Buffer.Length);
        }
        if (elements == -1)
        {
            if (!reader.DataAvailable || reader.Buffer[0] != 0xff)
            {
                throw new InvalidDataException("Invalid Conway reference script.");
            }
            reader.SkipDataItem();
        }
        if (reader.DataAvailable)
        {
            throw new InvalidDataException("Invalid Conway reference script.");
        }
        return size;
    }

    /// <summary>Finds the least fee covering the transaction, with the fee field encoded at that amount.</summary>
    /// <remarks>
    /// Supply the reference-script size after resolving the inputs. For added keys, the quote uses
    /// Chrysalis <c>Sign</c>'s definite witness map and tagged definite VKey list, retaining the
    /// original Alonzo witness classes too. A signer using other framing may produce a different size.
    /// </remarks>
    public static ulong MinFee(ITransaction tx, ProtocolParams pp, int extraVKeyWitnesses = 0, ulong referenceScriptSize = 0)
    {
        ArgumentNullException.ThrowIfNull(pp);
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentOutOfRangeException.ThrowIfNegative(extraVKeyWitnesses);
        if (tx is not PostMaryTransaction envelope)
        {
            throw new ArgumentException("Fee pricing requires a post-Mary transaction envelope.", nameof(tx));
        }
        ulong? witnessLength = extraVKeyWitnesses == 0 ? null : DraftSize.SignedWitnessLength(envelope.Witnesses, extraVKeyWitnesses);
        return FeeSolver.Solve(FeeSolver.Prepare(envelope, pp, new FeeInputs(ReferenceScriptSize: referenceScriptSize), witnessLength)).Fee;
    }

    /// <summary>Computes minimum lovelace from the output's actual encoded size (Babbage onward, including list-form outputs).</summary>
    public static ulong MinLovelace(ITransactionOutput output, ulong coinsPerUtxoByte)
    {
        ArgumentNullException.ThrowIfNull(output);
        return checked((ulong)MinimumOutput(DraftSize.EncodedLength(output), coinsPerUtxoByte));
    }

    /// <summary>Finds the least coin no smaller than the output's coin that covers its own encoded size (Babbage onward).</summary>
    /// <remarks>An already funded output is left alone. Only the coin's width may change, not its tokens or other fields.</remarks>
    public static ulong LeastValidCoin(ITransactionOutput output, ulong coinsPerUtxoByte)
    {
        ArgumentNullException.ThrowIfNull(output);
        IValue amount = output.Amount();
        ulong coin = amount.Lovelace();
        ulong length = DraftSize.EncodedLength(output);
        if (coin >= MinimumOutput(length, coinsPerUtxoByte))
        {
            return coin;
        }
        ulong fixedLength = checked(length - DraftSize.CoinWidth(amount));
        // Required coin is constant within a width and never decreases as width grows.
        // Jumping to that requirement cannot skip a feasible coin, and there are only five widths.
        while (true)
        {
            UInt128 minimum = MinimumOutput(checked(fixedLength + DraftSize.Width(coin)), coinsPerUtxoByte);
            if (coin >= minimum)
            {
                return coin;
            }
            coin = checked((ulong)minimum);
        }
    }

    /// <summary>Computes the collateral requirement, rounding the percentage up to a whole lovelace.</summary>
    public static ulong RequiredCollateral(ulong fee, ulong collateralPercentage) => checked((ulong)ExactFee.Collateral(fee, collateralPercentage));

    internal static UInt128 MinimumOutput(ulong bytes, ulong cost) => checked(((UInt128)160 + bytes) * cost);

    internal static T Parameter<T>(T? value, string name) where T : struct => value
        ?? throw new InvalidDataException($"Missing protocol parameter {name}.");

    private sealed class OutrefComparer : IEqualityComparer<TransactionInput>
    {
        internal static readonly OutrefComparer Instance = new();

        public bool Equals(TransactionInput x, TransactionInput y) => x.Index == y.Index && x.TransactionId.Span.SequenceEqual(y.TransactionId.Span);

        public int GetHashCode(TransactionInput input)
        {
            HashCode hash = new();
            hash.AddBytes(input.TransactionId.Span);
            hash.Add(input.Index);
            return hash.ToHashCode();
        }
    }
}
