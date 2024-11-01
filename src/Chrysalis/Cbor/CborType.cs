namespace Chrysalis.Cbor;
public enum CborType
{
    Constr,
    Bytes,
    Bool,
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