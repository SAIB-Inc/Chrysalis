using Chrysalis.Codec.Types.Cardano.Core.Certificates;
using Chrysalis.Codec.Types.Cardano.Core.Governance;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;

namespace Chrysalis.Codec.Serialization.Utils;

// Wire-backed records include their source slices in generated record equality. Ledger keys
// compare decoded values, including byte strings from different slices or different wire forms.
internal static class LedgerKeyComparers
{
    internal static readonly EqualityComparer<RewardAccount> RewardAccount = EqualityComparer<RewardAccount>.Create(
        static (a, b) => ReferenceEquals(a, b) || (a is not null && b is not null && a.Value.Span.SequenceEqual(b.Value.Span)),
        static key => Hash(key.Value));

    internal static readonly EqualityComparer<Credential> Credential = EqualityComparer<Credential>.Create(
        static (a, b) => a.Type == b.Type && a.Hash.Span.SequenceEqual(b.Hash.Span),
        static key => HashCode.Combine(key.Type, Hash(key.Hash)));

    internal static readonly EqualityComparer<Voter> Voter = EqualityComparer<Voter>.Create(
        static (a, b) => a.Tag == b.Tag && a.Hash.Span.SequenceEqual(b.Hash.Span),
        static key => HashCode.Combine(key.Tag, Hash(key.Hash)));

    internal static readonly EqualityComparer<GovActionId> GovActionId = EqualityComparer<GovActionId>.Create(
        static (a, b) => a.GovActionIndex == b.GovActionIndex && a.TransactionId.Span.SequenceEqual(b.TransactionId.Span),
        static key => HashCode.Combine(key.GovActionIndex, Hash(key.TransactionId)));

    internal static readonly EqualityComparer<RedeemerKey> RedeemerKey = EqualityComparer<RedeemerKey>.Create(
        static (a, b) => a.Tag == b.Tag && a.Index == b.Index, static key => HashCode.Combine(key.Tag, key.Index));

    private static int Hash(ReadOnlyMemory<byte> value) => ReadOnlyMemoryComparer.Instance.GetHashCode(value);
}
