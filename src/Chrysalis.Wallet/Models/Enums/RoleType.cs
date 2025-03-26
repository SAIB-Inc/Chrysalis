namespace Chrysalis.Wallet.Models.Enums;
public enum RoleType
{
    /// <summary>
    /// External Chain (0): Used for receiving payments; corresponds to the 'External' chain in BIP44.
    /// </summary>
    ExternalChain = 0,

    /// <summary>
    /// Internal Chain (1): Used for change addresses; corresponds to the 'Internal' chain in BIP44.
    /// </summary>
    InternalChain = 1,

    /// <summary>
    /// Staking Key (2): Associated with staking credentials as defined in CIP-0011.
    /// </summary>
    Staking = 2,

    /// <summary>
    /// DRep Key (3): Pertains to Delegate Representative keys as outlined in CIP-0105.
    /// </summary>
    DRep = 3,

    /// <summary>
    /// Constitutional Committee Cold Key (4): Refers to cold keys for the Constitutional Committee as specified in CIP-0105.
    /// </summary>
    ConstitutionalCommitteeColdKey = 4,

    /// <summary>
    /// Constitutional Committee Hot Key (5): Refers to hot keys for the Constitutional Committee as specified in CIP-0105.
    /// </summary>
    ConstitutionalCommitteeHotKey = 5
}