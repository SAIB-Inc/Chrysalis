namespace Chrysalis.Cbor;
public enum CborType
{
    Constr,
    Bytes,
    Int,
    Ulong,
    Long,
    Map,
    List,
    Union,
    EncodedValue,
    RationalNumber,
    Text,
    Nullable,
    Tag
}