using Chrysalis.Cardano.Core.Types.Block.Transaction.Output;
using Chrysalis.Cardano.Crashr.Types.Common;
using Chrysalis.Cardano.Crashr.Types.Datums;
using Chrysalis.Cbor.Types.Collections;
using Chrysalis.Cbor.Types.Functional;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Plutus.Types.Address;
using Xunit;

namespace Chrysalis.Test.Types.Cardano.Crashr;

public record InheritedCrashrListingDatum(
    CborList<Payout> Payouts,
    CborBytes Owner
) : ListingDatum(Payouts, Owner);

public record NestedInheritedCrashrListingDatum(
    CborList<Payout> Payouts,
    CborBytes Owner
) : InheritedCrashrListingDatum(Payouts, Owner);



public class CrashrTestData
{
    public readonly static string _testTag = "CborBool";

    public static IEnumerable<object[]> TestData
    {
        get
        {
            ListingDatum listingDatum = new(
                new CborList<Payout>(
                    [
                            new Payout(
                                new(
                                    new VerificationKey(new(Convert.FromHexString("7060251a30c40e428085cdb477aa0ca8462ae5d8b6a55e5e9616aeb6"))),
                                    new Some<Inline<Credential>>(new Inline<Credential>(new VerificationKey(new(Convert.FromHexString("850282937506f53d58e9e6fd7ccbbbc57196d58b2a11e36a76a60857"))))
                                    )
                                ),
                                new(
                                    new()
                                    {
                                        {
                                            new(Convert.FromHexString("")),
                                            new(
                                                new Dictionary<CborBytes, CborUlong>
                                                {
                                                    {
                                                        new(Convert.FromHexString("")),
                                                        new(48000000)
                                                    }
                                                }
                                            )
                                        }
                                    }
                                )
                            )
                        ]
                ),
                new CborBytes(Convert.FromHexString("7060251a30c40e428085cdb477aa0ca8462ae5d8b6a55e5e9616aeb6"))
            );

            string listingDatumCbor = "d8799f9fd8799fd8799fd8799f581c7060251a30c40e428085cdb477aa0ca8462ae5d8b6a55e5e9616aeb6ffd8799fd8799fd8799f581c850282937506f53d58e9e6fd7ccbbbc57196d58b2a11e36a76a60857ffffffffa140a1401a02dc6c00ffff581c7060251a30c40e428085cdb477aa0ca8462ae5d8b6a55e5e9616aeb6ff";

            listingDatum.Raw = Convert.FromHexString(listingDatumCbor);

            InheritedCrashrListingDatum inheritedListingDatum = new(
                listingDatum.Payouts,
                listingDatum.Owner
            );

            inheritedListingDatum.Raw = Convert.FromHexString(listingDatumCbor);

            NestedInheritedCrashrListingDatum nestedInheritedListingDatum = new(
                listingDatum.Payouts,
                listingDatum.Owner
            );

            nestedInheritedListingDatum.Raw = Convert.FromHexString(listingDatumCbor);

            yield return new object[] { $"[{_testTag}] Basic", listingDatumCbor, listingDatum };
            yield return new object[] { $"[{_testTag}] Inherited", listingDatumCbor, inheritedListingDatum };
            yield return new object[] { $"[{_testTag}] Nested Inherited", listingDatumCbor, nestedInheritedListingDatum };
        }
    }

    public static IEnumerable<object[]> GetTestData() => TestData;

    public static void AssertListingDatumsEqual(ListingDatum expected, ListingDatum actual)
    {
        // Compare Owner bytes
        Assert.True(expected.Owner.Value.SequenceEqual(actual.Owner.Value));

        // Compare Payouts list length
        Assert.Equal(expected.Payouts.Value.Count, actual.Payouts.Value.Count);

        // Raw Cbor Check
        Assert.True(expected.Raw!.SequenceEqual(actual.Raw!));

        // Compare each payout
        for (int i = 0; i < expected.Payouts.Value.Count; i++)
        {
            Payout expectedPayout = expected.Payouts.Value[i];
            Payout actualPayout = actual.Payouts.Value[i];

            byte[] expectedPkh = expectedPayout.Address.PaymentCredential switch
            {
                VerificationKey vk => vk.VerificationKeyHash.Value,
                _ => throw new Exception("Unexpected PaymentCredential type")
            };

            byte[] actualPkh = actualPayout.Address.PaymentCredential switch
            {
                VerificationKey vk => vk.VerificationKeyHash.Value,
                _ => throw new Exception("Unexpected PaymentCredential type")
            };

            byte[] skh = expectedPayout.Address.StakeCredential switch
            {
                Option<Inline<Credential>> option => option switch
                {
                    Some<Inline<Credential>> some => some.Value switch
                    {
                        Inline<Credential> inline => inline.Value switch
                        {
                            VerificationKey vk => vk.VerificationKeyHash.Value,
                            _ => throw new Exception("Unexpected Credential type")
                        },
                        _ => throw new Exception("Unexpected Inline type")
                    },
                    _ => throw new Exception("Unexpected Option type")
                },
                _ => throw new Exception("Unexpected StakeCredential type")
            };

            byte[] actualSkh = actualPayout.Address.StakeCredential switch
            {
                Option<Inline<Credential>> option => option switch
                {
                    Some<Inline<Credential>> some => some.Value switch
                    {
                        Inline<Credential> inline => inline.Value switch
                        {
                            VerificationKey vk => vk.VerificationKeyHash.Value,
                            _ => throw new Exception("Unexpected Credential type")
                        },
                        _ => throw new Exception("Unexpected Inline type")
                    },
                    _ => throw new Exception("Unexpected Option type")
                },
                _ => throw new Exception("Unexpected StakeCredential type")
            };

            // Compare address verification key
            Assert.True(
                expectedPkh.SequenceEqual(actualPkh),
                $"Expected: {BitConverter.ToString(expectedPkh)}\nActual: {BitConverter.ToString(actualPkh)}"
            );

            // Compare address stake key
            Assert.True(
                skh.SequenceEqual(actualSkh),
                $"Expected: {BitConverter.ToString(skh)}\nActual: {BitConverter.ToString(actualSkh)}"
            );

            // Compare amount dictionary
            Assert.Equal(expectedPayout.Amount.Value.Count, actualPayout.Amount.Value.Count);
            foreach (KeyValuePair<CborBytes, TokenBundleOutput> kvp in expectedPayout.Amount.Value)
            {
                Assert.Contains(Convert.ToHexString(kvp.Key.Value), actualPayout.Amount.Value.Keys.Select(e => Convert.ToHexString(e.Value)));
                Assert.True(kvp.Key.Value.SequenceEqual(actualPayout.Amount.Value.Keys.First().Value));
            }
        }
    }
}