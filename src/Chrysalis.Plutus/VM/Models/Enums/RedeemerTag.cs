namespace Chrysalis.Plutus.VM.Models.Enums;

/// <summary>
/// Represents the tag identifying the purpose of a Plutus script redeemer.
/// </summary>
public enum RedeemerTag
{
    /// <summary>Redeemer for spending a UTxO.</summary>
    Spend = 0,

    /// <summary>Redeemer for minting or burning tokens.</summary>
    Mint = 1,

    /// <summary>Redeemer for certificate operations.</summary>
    Cert = 2,

    /// <summary>Redeemer for reward withdrawals.</summary>
    Reward = 3,

    /// <summary>Redeemer for governance voting.</summary>
    Vote = 4,

    /// <summary>Redeemer for governance proposals.</summary>
    Propose = 5,
}
