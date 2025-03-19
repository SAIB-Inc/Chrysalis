Let's have a clear set of instructions:

We have different "container" types, this is the top-level types:
CborMap -> { 0: value,  1: diffTypeValue }
CborList -> [value, diffTypeValue] 
CborConstr -> if it's a cbor constructor that needs to have the cbor tag
Map -> { key : value} same type for keys and same type for values
List -> [values]
Container -> Container(value) only 1 value inside, could be primitive or not
Primitives -> int, long, ulong, byte[] etc.

Now we also have a special attribute [CborNullable] value

if this is attached to a property we will serialize it to null if it's null value, otherwise we serialize the value to it's correct shape.

Can we try to re-implement everything, with these simple rules? 

Attributes available as helper for the source gen:

class/record level

[CborSerializable] -> only those types with this attribute will be source generated
[CborMap] -> if it's a cbormap
[CborList] -> if it's a cborlist
[CborConstr(index)] -> if it's a cbor constr

property level
[CborNullable] -> if it's a nullable type, nullable types are different from int? etc. cbor nullable means on the cbor level it's null
[CborOrder(int)] -> for cborlist to know the order of serialization/deserialization
[CborProperty(int/string)] -> for cbormap keys
[CborSize(int)] -> for byte[] to know if it will be serialized as definite/indefinte length

we have these very clear rule, we must be able to create something that just works. 