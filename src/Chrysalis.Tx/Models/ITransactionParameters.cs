namespace Chrysalis.Tx.Models;

/// <summary>
/// Interface for transaction parameters that include party address mappings.
/// </summary>
public interface ITransactionParameters
{
    /// <summary>
    /// Gets or sets the party identifier to address mapping with change designation.
    /// </summary>
    Dictionary<string, (string address, bool isChange)> Parties { get; }
}
