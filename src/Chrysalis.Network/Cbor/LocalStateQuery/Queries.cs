using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Network.Cbor.LocalStateQuery;

public enum QueryEra
{
    Byron = 0,
    Shelley = 1,
    Allegra = 2,
    Mary = 3,
    Alonzo = 4,
    Babbage = 5,
    Conway = 6,
}

public static class Queries
{
    public static ShellyQuery CreateShellyQuery(CborBase query)
    {
        return new ShellyQuery(
            new CborUlong(0),
            new ShellyQuery(
                new CborUlong(0),
                new ShellyQuery(
                    // @TODO: This is currently hardcoded. We need to probably make this dynamic
                    // by querying the current era from the ledger.
                    new CborUlong((ulong)QueryEra.Conway),
                    new ShellyQuery(
                        query,
                        null
                    )
                )
            )
        );
    }

    public static ShellyQuery GetCurrentEra =>
        new ShellyQuery(
            new CborUlong(0),
            new ShellyQuery(
                new CborUlong(2),
                null
            )
        );

    public static ShellyQuery GetTip => CreateShellyQuery(new CborUlong(0));
    public static ShellyQuery GetUtxoByAddress(List<byte[]> addresses)
    {
        var cborAddress = addresses.Select(a => new CborBytes(a)).ToList();
        return CreateShellyQuery(new UtxoByAddressQuery(new(6), new TaggedAddresses(cborAddress)));
    }
}

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record BasicQuery([CborIndex(0)] CborUlong Idx) : CborBase;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record ShellyQuery(
    [CborIndex(0)] CborBase Query,
    [CborIndex(1)] ShellyQuery? InnerQuery
) : CborBase;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record UtxoByAddressQuery(
    [CborIndex(0)] CborUlong Idx,
    [CborIndex(1)] TaggedAddresses Addresses
) : CborBase;

[CborConverter(typeof(ListConverter))]
[CborOptions(IsDefinite = true, Tag = 258)]
public record TaggedAddresses(List<CborBytes> Addresses) : CborBase;