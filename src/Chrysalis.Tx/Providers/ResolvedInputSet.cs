using Chrysalis.Codec.Serialization;
using Chrysalis.Tx.Models.Cbor;

namespace Chrysalis.Tx.Providers;

/// <summary>Collects resolved inputs in encounter order, checking repeated output identities.</summary>
/// <param name="provider">The fixed provider name.</param>
/// <param name="operation">The fixed operation name.</param>
public sealed class ResolvedInputSet(string provider, string operation)
{
    private readonly Dictionary<(string Hash, ulong Index), (string Request, ResolvedInput Input)> _inputs = [];
    private readonly Dictionary<(string Hash, ulong Index), HashSet<string>> _overlap = [];
    private readonly List<ResolvedInput> _ordered = [];

    /// <summary>Adds an input from a request, or rejects a repeated or conflicting answer.</summary>
    public void Add(string request, ResolvedInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        (string, ulong) key = (Convert.ToHexString(input.Outref.TransactionId.Span), input.Outref.Index);
        if (_inputs.TryAdd(key, (request, input)))
        {
            _ordered.Add(input);
            return;
        }
        (string previous, ResolvedInput existing) = _inputs[key];
        if (!_overlap.TryGetValue(key, out HashSet<string>? requests))
        {
            requests = [with(StringComparer.Ordinal), previous];
            _overlap.Add(key, requests);
        }
        if (!requests.Add(request))
        {
            throw ProviderHttp.Unusable(provider, operation, "repeated output in one request");
        }
        if (!CborSerializer.Serialize(existing.Output).AsSpan().SequenceEqual(CborSerializer.Serialize(input.Output)))
        {
            throw ProviderHttp.Unusable(provider, operation, "conflicting output across requests");
        }
    }

    /// <summary>Returns the inputs in encounter order.</summary>
    public List<ResolvedInput> ToList() => [.. _ordered];
}
