using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;

namespace Chrysalis.Tx.Builders;

/// <summary>
/// Builds redeemers with deferred index computation.
/// Redeemers are stored untagged and indexed during Build() based on
/// the sorted order of inputs/policies/addresses — matching Cardano ledger spec.
/// </summary>
public sealed class RedeemerBuilder
{
    private readonly SortedDictionary<string, UntaggedRedeemer> _spend = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, UntaggedRedeemer> _mint = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, UntaggedRedeemer> _reward = new(StringComparer.Ordinal);
    private readonly List<UntaggedRedeemer?> _cert = [];

    /// <summary>
    /// Returns true if any redeemers have been added.
    /// </summary>
    public bool HasRedeemers => _spend.Count > 0 || _mint.Count > 0 || _reward.Count > 0 || _cert.Count > 0;

    /// <summary>
    /// Adds a spend redeemer for an input. Key is tx_hash + index for lexicographic ordering.
    /// </summary>
    public RedeemerBuilder AddSpend(InputBuilderResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Requirements.RedeemerData is not null)
        {
            string key = Convert.ToHexString(result.Input.TransactionId.Span)
                + result.Input.Index.ToString("D10", System.Globalization.CultureInfo.InvariantCulture);
            _spend[key] = new UntaggedRedeemer(result.Requirements.RedeemerData);
        }

        return this;
    }

    /// <summary>
    /// Adds a mint redeemer for a policy. Key is the policy hash for lexicographic ordering.
    /// </summary>
    public RedeemerBuilder AddMint(string policyIdHex, IPlutusData redeemer)
    {
        ArgumentNullException.ThrowIfNull(policyIdHex);
        _mint[policyIdHex.ToUpperInvariant()] = new UntaggedRedeemer(redeemer);
        return this;
    }

    /// <summary>
    /// Adds a reward withdrawal redeemer. Key is the reward address for ordering.
    /// </summary>
    public RedeemerBuilder AddReward(string rewardAddressHex, IPlutusData redeemer)
    {
        ArgumentNullException.ThrowIfNull(rewardAddressHex);
        _reward[rewardAddressHex.ToUpperInvariant()] = new UntaggedRedeemer(redeemer);
        return this;
    }

    /// <summary>
    /// Adds a certificate redeemer at the given position.
    /// </summary>
    public RedeemerBuilder AddCert(int position, IPlutusData redeemer)
    {
        while (_cert.Count <= position)
        {
            _cert.Add(null);
        }

        _cert[position] = new UntaggedRedeemer(redeemer);
        return this;
    }

    /// <summary>
    /// Updates execution units for a redeemer after script evaluation.
    /// </summary>
    public RedeemerBuilder UpdateExUnits(int tag, int index, ExUnits exUnits)
    {
        IEnumerable<UntaggedRedeemer> redeemers = tag switch
        {
            0 => _spend.Values,
            1 => _mint.Values,
            2 => _reward.Values,
            3 => _cert.OfType<UntaggedRedeemer>(),
            _ => []
        };

        UntaggedRedeemer? target = redeemers.ElementAtOrDefault(index);
        target?.SetExUnits(exUnits);
        return this;
    }

    /// <summary>
    /// Builds the final IRedeemers with auto-computed indices from sorted key order.
    /// </summary>
    public IRedeemers Build()
    {
        List<RedeemerEntry> entries = [];

        ulong idx = 0;
        foreach (UntaggedRedeemer redeemer in _spend.Values)
        {
            entries.Add(RedeemerEntry.Create(0, idx++, redeemer.Data, redeemer.ExUnits ?? DefaultExUnits()));
        }

        idx = 0;
        foreach (UntaggedRedeemer redeemer in _mint.Values)
        {
            entries.Add(RedeemerEntry.Create(1, idx++, redeemer.Data, redeemer.ExUnits ?? DefaultExUnits()));
        }

        idx = 0;
        foreach (UntaggedRedeemer redeemer in _reward.Values)
        {
            entries.Add(RedeemerEntry.Create(2, idx++, redeemer.Data, redeemer.ExUnits ?? DefaultExUnits()));
        }

        idx = 0;
        foreach (UntaggedRedeemer? redeemer in _cert)
        {
            if (redeemer is not null)
            {
                entries.Add(RedeemerEntry.Create(3, idx, redeemer.Data, redeemer.ExUnits ?? DefaultExUnits()));
            }

            idx++;
        }

        return RedeemerList.Create(entries);
    }

    private static ExUnits DefaultExUnits() => ExUnits.Create(0, 0);

    /// <summary>
    /// Untagged redeemer — stores data and optional execution units (set after evaluation).
    /// </summary>
    internal sealed class UntaggedRedeemer(IPlutusData data)
    {
        public IPlutusData Data { get; } = data;
        public ExUnits? ExUnits { get; private set; }

        public void SetExUnits(ExUnits units) => ExUnits = units;
    }
}
