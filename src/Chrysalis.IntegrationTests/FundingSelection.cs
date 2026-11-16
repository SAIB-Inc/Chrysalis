using System.Globalization;
using System.Text.Json;
using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Models.Cbor;
using Xunit;

namespace Chrysalis.IntegrationTests;

internal sealed record FundingPlan(ResolvedInput Selected, List<ResolvedInput> ProviderInputs);

internal static class FundingSelection
{
    internal static FundingPlan CreatePlan(List<ResolvedInput> available, TransactionInput collateral, JsonElement unfilteredWalletUtxos)
    {
        ArgumentNullException.ThrowIfNull(available);
        RequireCollateralPresent(unfilteredWalletUtxos, collateral);
        string protectedInput = Format(collateral);
        ResolvedInput selected = available.Where(input => Format(input.Outref) != protectedInput && input.Output.Amount().Lovelace() >= 5_000_000)
            .OrderBy(input => input.Output.Amount().Lovelace()).FirstOrDefault()
            ?? throw new InvalidOperationException("No non-collateral ADA-only input of at least 5 ADA is available.");
        // The balancing provider must not discover any additional wallet input.
        return new FundingPlan(selected, [selected]);
    }

    internal static void RequireCollateralPresent(JsonElement unfilteredWalletUtxos, TransactionInput collateral)
    {
        string reference = Format(collateral);
        // Look at the original CLI map, not ADA-only spending candidates: collateral may hold tokens.
        if (unfilteredWalletUtxos.ValueKind != JsonValueKind.Object
            || !unfilteredWalletUtxos.EnumerateObject().Any(output => string.Equals(output.Name, reference, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"{IntegrationEnvironment.CollateralTxIn} is absent from the wallet's unfiltered CLI UTxO map; verify the current public wallet address and collateral txid#index.");
        }
    }

    internal static TransactionInput ParseInputReference(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string[] parts = value.Split('#');
        if (parts.Length != 2 || parts[0].Length != 64 || !parts[0].All(Uri.IsHexDigit)
            || !ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out ulong index))
        {
            throw new InvalidOperationException("A public transaction input must use a 32-byte hex transaction id followed by # and an unsigned index.");
        }
        return TransactionInput.Create(Convert.FromHexString(parts[0]), index);
    }

    internal static void AssertExactlySelected(IEnumerable<TransactionInput> actual, IEnumerable<TransactionInput> selected)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(selected);
        // Sorted sequences also reject duplicate inputs, unlike set-only equality.
        Assert.Equal(selected.Select(Format).Order(StringComparer.Ordinal), actual.Select(Format).Order(StringComparer.Ordinal));
    }

    private static string Format(TransactionInput input) => Convert.ToHexStringLower(input.TransactionId.Span) + "#" + input.Index.ToString(CultureInfo.InvariantCulture);
}
