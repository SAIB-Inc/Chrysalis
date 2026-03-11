using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;

namespace Chrysalis.Tx.Cli;

// Wizard validator redeemer
[CborSerializable]
[CborUnion]
public abstract partial record WizardRedeemer : CborRecord;

[CborSerializable]
[CborConstr(0)]
public partial record BuyRedeemer(
    long OutputIndex,
    PlutusBool OfferSecond,
    ICborOption<OracleFeeds> OracleFeeds
) : WizardRedeemer;

[CborSerializable]
[CborConstr(1)]
public partial record CloseRedeemer : WizardRedeemer;

// Aiken Bool: Constr(0,[]) = False, Constr(1,[]) = True
[CborSerializable]
[CborUnion]
public abstract partial record PlutusBool : CborRecord;

[CborSerializable]
[CborConstr(0)]
public partial record PlutusFalse : PlutusBool;

[CborSerializable]
[CborConstr(1)]
public partial record PlutusTrue : PlutusBool;

// Oracle feeds
[CborSerializable]
[CborList]
public partial record OracleFeeds(SignedPriceFeed OfferedFeed, SignedPriceFeed ReceivedFeed) : CborRecord;

[CborSerializable]
[CborConstr(0)]
[CborIndefinite]
public partial record PriceFeed(long Price, [CborSize(64)] byte[] Name, long Timestamp) : CborRecord;

[CborSerializable]
[CborConstr(0)]
[CborIndefinite]
public partial record SignedPriceFeed(PriceFeed Data, byte[] Signature) : CborRecord;

// Wizard datum types
[CborSerializable]
[CborUnion]
public abstract partial record OrderKind : CborRecord;

[CborSerializable]
[CborConstr(0)]
public partial record AutoLimit : OrderKind;

[CborSerializable]
[CborConstr(1)]
public partial record FixedPriceKind : OrderKind;

[CborSerializable]
[CborList]
public partial record WizardAsset(byte[] PolicyId, byte[] AssetName) : CborRecord;

[CborSerializable]
[CborList]
public partial record AssetPair(WizardAsset First, WizardAsset Second) : CborRecord;

[CborSerializable]
[CborConstr(0)]
public partial record RationalC(long Num, long Den) : CborRecord;

[CborSerializable]
[CborUnion]
public abstract partial record Swap : CborRecord;

[CborSerializable]
[CborConstr(0)]
public partial record OneWay(RationalC Rational) : Swap;

[CborSerializable]
[CborConstr(1)]
public partial record TwoWays(RationalC Rational1, RationalC Rational2) : Swap;

// MultisigScript owner types
[CborSerializable]
[CborUnion]
public abstract partial record MultisigScript : CborRecord;

[CborSerializable]
[CborConstr(0)]
public partial record Signature(byte[] KeyHash) : MultisigScript;

[CborSerializable]
[CborConstr(1)]
public partial record AllOf(CborIndefList<MultisigScript> Scripts) : MultisigScript;

[CborSerializable]
[CborConstr(2)]
public partial record AnyOf(CborIndefList<MultisigScript> Scripts) : MultisigScript;

[CborSerializable]
[CborConstr(3)]
public partial record AtLeast(ulong Required, CborIndefList<MultisigScript> Scripts) : MultisigScript;

[CborSerializable]
[CborConstr(4)]
public partial record Before(long Time) : MultisigScript;

[CborSerializable]
[CborConstr(5)]
public partial record After(long Time) : MultisigScript;

[CborSerializable]
[CborConstr(6)]
public partial record ScriptCredentialMs(byte[] ScriptHash) : MultisigScript;

// Wizard datum
[CborSerializable]
[CborConstr(0)]
public partial record WizardDatum(
    OrderKind Kind,
    AssetPair AssetPair,
    Swap SwapPrice,
    ICborOption<Swap> MinimumPrice,
    MultisigScript Owner
) : CborRecord;

// Template wrapper: ["wizard", datum]
[CborSerializable]
[CborList]
public partial record TemplateWizardDatum(byte[] Tag, WizardDatum Datum) : CborRecord;
