// CBOR Source Generator Metadata Debug Information
// Generated: 03/17/2025 22:02:32
// Context Type: global::Chrysalis.Cbor.Types.Test.TestListMaybe

// Types Count: 9

// ======================================================
// Type: global::Chrysalis.Cbor.Types.Test.TestListMaybe
// ======================================================
//   Format: Array
//   Tag: 
//   IsIndefinite: False
//   Constructor: 
//   ValidatorTypeName: None
//   HasValidator: False
//   Properties: 1

// ----- DETAILED DEBUG INFO -----
// Extracting metadata for type: Chrysalis.Cbor.Types.Test.TestListMaybe
// Processing attributes for type: Chrysalis.Cbor.Types.Test.TestListMaybe
//   Attribute: Chrysalis.Cbor.Serialization.Attributes.CborSerializableAttribute
//   Attribute: Chrysalis.Cbor.Serialization.Attributes.CborListAttribute
//     Set format to Array
// Detecting validator for: Chrysalis.Cbor.Types.Test.TestListMaybe
//    Interface: System.IEquatable<Chrysalis.Cbor.Types.CborBase<Chrysalis.Cbor.Types.Test.TestListMaybe>>, IsValidator: False
//    Interface: System.IEquatable<Chrysalis.Cbor.Types.Test.TestListMaybe>, IsValidator: False
//    Searching for validator named: TestListMaybeValidator
//    Checking: Chrysalis.Cbor.Types.Test.TestListMaybeValidator
//    Checking: Chrysalis.Cbor.Types.Test.Validators.TestListMaybeValidator
//    Checking: Chrysalis.Cbor.Types.Validators.TestListMaybeValidator
//    Checking: Chrysalis.Cbor.Serialization.Validators.TestListMaybeValidator
//    Performing broader search for: TestListMaybeValidator
// Final validator detection result: None
// Extracted 1 properties
// Extracted 1 constructor parameters
// Final metadata extraction result:
//   Format: Array
//   ValidatorTypeName: None
//   HasValidator: False

// ----- END DETAILED DEBUG INFO -----

//     Property: Value1
//       Type: global::Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>
//       Key: Value1
//       IsCborNullable: False
//       IsPropertyNullable: True
//       Order: 
//       IsCollection: False
//       IsDictionary: False

// ======================================================
// Type: global::Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>
// ======================================================
//   Format: Union
//   Tag: 
//   IsIndefinite: False
//   Constructor: 
//   ValidatorTypeName: None
//   HasValidator: False
//   Properties: 0

// ----- DETAILED DEBUG INFO -----
// Extracting metadata for type: Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>
// Processing attributes for type: Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>
//   Attribute: Chrysalis.Cbor.Serialization.Attributes.CborSerializableAttribute
//   Attribute: Chrysalis.Cbor.Serialization.Attributes.CborUnionAttribute
//     Set format to Union
// Detecting validator for: Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>
//    Interface: System.IEquatable<Chrysalis.Cbor.Types.CborBase<Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>>>, IsValidator: False
//    Interface: System.IEquatable<Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>>, IsValidator: False
//    Searching for validator named: CborMaybeIndefListValidator
//    Checking: Chrysalis.Cbor.Types.Custom.CborMaybeIndefListValidator
//    Checking: Chrysalis.Cbor.Types.Custom.Validators.CborMaybeIndefListValidator
//    Checking: Chrysalis.Cbor.Types.Validators.CborMaybeIndefListValidator
//    Checking: Chrysalis.Cbor.Serialization.Validators.CborMaybeIndefListValidator
//    Performing broader search for: CborMaybeIndefListValidator
// Final validator detection result: None
// Extracted 0 properties
// Extracted 0 constructor parameters
// Extracted 4 union cases
// Final metadata extraction result:
//   Format: Union
//   ValidatorTypeName: None
//   HasValidator: False

// ----- END DETAILED DEBUG INFO -----


//   Union Cases: 4
//     Case: global::Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>.CborDefList
//     Case: global::Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>.CborIndefList
//     Case: global::Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>.CborDefListWithTag
//     Case: global::Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>.CborIndefListWithTag

// ======================================================
// Type: global::Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>.CborDefList
// ======================================================
//   Format: Object
//   Tag: 
//   IsIndefinite: False
//   Constructor: 
//   ValidatorTypeName: None
//   HasValidator: False
//   Properties: 1

// ----- DETAILED DEBUG INFO -----
// Extracting metadata for type: Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>.CborDefList
// Processing attributes for type: Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>.CborDefList
//   Attribute: Chrysalis.Cbor.Serialization.Attributes.CborSerializableAttribute
// Detecting validator for: Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>.CborDefList
//    Interface: System.IEquatable<Chrysalis.Cbor.Types.CborBase<Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>>>, IsValidator: False
//    Interface: System.IEquatable<Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>>, IsValidator: False
//    Interface: System.IEquatable<Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>.CborDefList>, IsValidator: False
//    Searching for validator named: CborDefListValidator
//    Checking: Chrysalis.Cbor.Types.Custom.CborDefListValidator
//    Checking: Chrysalis.Cbor.Types.Custom.Validators.CborDefListValidator
//    Checking: Chrysalis.Cbor.Types.Validators.CborDefListValidator
//    Checking: Chrysalis.Cbor.Serialization.Validators.CborDefListValidator
//    Performing broader search for: CborDefListValidator
// Final validator detection result: None
// Extracted 1 properties
// Extracted 1 constructor parameters
// Final metadata extraction result:
//   Format: Object
//   ValidatorTypeName: None
//   HasValidator: False

// ----- END DETAILED DEBUG INFO -----

//     Property: Value
//       Type: global::System.Collections.Generic.List<int>
//       Key: Value
//       IsCborNullable: False
//       IsPropertyNullable: True
//       Order: 
//       IsCollection: True
//       IsDictionary: False
//       ElementType: int

// ======================================================
// Type: global::Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>.CborIndefList
// ======================================================
//   Format: Object
//   Tag: 
//   IsIndefinite: False
//   Constructor: 
//   ValidatorTypeName: None
//   HasValidator: False
//   Properties: 1

// ----- DETAILED DEBUG INFO -----
// Extracting metadata for type: Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>.CborIndefList
// Processing attributes for type: Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>.CborIndefList
//   Attribute: Chrysalis.Cbor.Serialization.Attributes.CborSerializableAttribute
// Detecting validator for: Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>.CborIndefList
//    Interface: System.IEquatable<Chrysalis.Cbor.Types.CborBase<Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>>>, IsValidator: False
//    Interface: System.IEquatable<Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>>, IsValidator: False
//    Interface: System.IEquatable<Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>.CborIndefList>, IsValidator: False
//    Searching for validator named: CborIndefListValidator
//    Checking: Chrysalis.Cbor.Types.Custom.CborIndefListValidator
//    Checking: Chrysalis.Cbor.Types.Custom.Validators.CborIndefListValidator
//    Checking: Chrysalis.Cbor.Types.Validators.CborIndefListValidator
//    Checking: Chrysalis.Cbor.Serialization.Validators.CborIndefListValidator
//    Performing broader search for: CborIndefListValidator
// Final validator detection result: None
// Extracted 1 properties
// Extracted 1 constructor parameters
// Final metadata extraction result:
//   Format: Object
//   ValidatorTypeName: None
//   HasValidator: False

// ----- END DETAILED DEBUG INFO -----

//     Property: Value
//       Type: global::System.Collections.Generic.List<int>
//       Key: Value
//       IsCborNullable: False
//       IsPropertyNullable: True
//       Order: 
//       IsCollection: True
//       IsDictionary: False
//       ElementType: int

// ======================================================
// Type: global::Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>.CborDefListWithTag
// ======================================================
//   Format: Object
//   Tag: 258
//   IsIndefinite: False
//   Constructor: 
//   ValidatorTypeName: None
//   HasValidator: False
//   Properties: 1

// ----- DETAILED DEBUG INFO -----
// Extracting metadata for type: Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>.CborDefListWithTag
// Processing attributes for type: Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>.CborDefListWithTag
//   Attribute: Chrysalis.Cbor.Serialization.Attributes.CborSerializableAttribute
//   Attribute: Chrysalis.Cbor.Serialization.Attributes.CborTagAttribute
//     Set tag: 258
// Detecting validator for: Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>.CborDefListWithTag
//    Interface: System.IEquatable<Chrysalis.Cbor.Types.CborBase<Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>>>, IsValidator: False
//    Interface: System.IEquatable<Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>>, IsValidator: False
//    Interface: System.IEquatable<Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>.CborDefListWithTag>, IsValidator: False
//    Searching for validator named: CborDefListWithTagValidator
//    Checking: Chrysalis.Cbor.Types.Custom.CborDefListWithTagValidator
//    Checking: Chrysalis.Cbor.Types.Custom.Validators.CborDefListWithTagValidator
//    Checking: Chrysalis.Cbor.Types.Validators.CborDefListWithTagValidator
//    Checking: Chrysalis.Cbor.Serialization.Validators.CborDefListWithTagValidator
//    Performing broader search for: CborDefListWithTagValidator
// Final validator detection result: None
// Extracted 1 properties
// Extracted 1 constructor parameters
// Final metadata extraction result:
//   Format: Object
//   ValidatorTypeName: None
//   HasValidator: False

// ----- END DETAILED DEBUG INFO -----

//     Property: Value
//       Type: global::System.Collections.Generic.List<int>
//       Key: Value
//       IsCborNullable: False
//       IsPropertyNullable: True
//       Order: 
//       IsCollection: True
//       IsDictionary: False
//       ElementType: int

// ======================================================
// Type: global::Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>.CborIndefListWithTag
// ======================================================
//   Format: Object
//   Tag: 258
//   IsIndefinite: False
//   Constructor: 
//   ValidatorTypeName: None
//   HasValidator: False
//   Properties: 1

// ----- DETAILED DEBUG INFO -----
// Extracting metadata for type: Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>.CborIndefListWithTag
// Processing attributes for type: Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>.CborIndefListWithTag
//   Attribute: Chrysalis.Cbor.Serialization.Attributes.CborSerializableAttribute
//   Attribute: Chrysalis.Cbor.Serialization.Attributes.CborTagAttribute
//     Set tag: 258
// Detecting validator for: Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>.CborIndefListWithTag
//    Interface: System.IEquatable<Chrysalis.Cbor.Types.CborBase<Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>>>, IsValidator: False
//    Interface: System.IEquatable<Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>>, IsValidator: False
//    Interface: System.IEquatable<Chrysalis.Cbor.Types.Custom.CborMaybeIndefList<int>.CborIndefListWithTag>, IsValidator: False
//    Searching for validator named: CborIndefListWithTagValidator
//    Checking: Chrysalis.Cbor.Types.Custom.CborIndefListWithTagValidator
//    Checking: Chrysalis.Cbor.Types.Custom.Validators.CborIndefListWithTagValidator
//    Checking: Chrysalis.Cbor.Types.Validators.CborIndefListWithTagValidator
//    Checking: Chrysalis.Cbor.Serialization.Validators.CborIndefListWithTagValidator
//    Performing broader search for: CborIndefListWithTagValidator
// Final validator detection result: None
// Extracted 1 properties
// Extracted 1 constructor parameters
// Final metadata extraction result:
//   Format: Object
//   ValidatorTypeName: None
//   HasValidator: False

// ----- END DETAILED DEBUG INFO -----

//     Property: Value
//       Type: global::System.Collections.Generic.List<int>
//       Key: Value
//       IsCborNullable: False
//       IsPropertyNullable: True
//       Order: 
//       IsCollection: True
//       IsDictionary: False
//       ElementType: int

// ======================================================
// Type: global::System.Collections.Generic.List<int>
// ======================================================
//   Format: Object
//   Tag: 
//   IsIndefinite: False
//   Constructor: 
//   ValidatorTypeName: None
//   HasValidator: False
//   Properties: 9

// ----- DETAILED DEBUG INFO -----
// Extracting metadata for type: System.Collections.Generic.List<int>
// Processing attributes for type: System.Collections.Generic.List<int>
//   Attribute: System.Runtime.CompilerServices.NullableContextAttribute
//   Attribute: System.Runtime.CompilerServices.NullableAttribute
//   Attribute: System.Reflection.DefaultMemberAttribute
// Detecting validator for: System.Collections.Generic.List<int>
//    Interface: System.Collections.Generic.IList<int>, IsValidator: False
//    Interface: System.Collections.Generic.ICollection<int>, IsValidator: False
//    Interface: System.Collections.Generic.IReadOnlyList<int>, IsValidator: False
//    Interface: System.Collections.Generic.IReadOnlyCollection<int>, IsValidator: False
//    Interface: System.Collections.Generic.IEnumerable<int>, IsValidator: False
//    Interface: System.Collections.IList, IsValidator: False
//    Interface: System.Collections.ICollection, IsValidator: False
//    Interface: System.Collections.IEnumerable, IsValidator: False
//    Searching for validator named: ListValidator
//    Checking: System.Collections.Generic.ListValidator
//    Checking: System.Collections.Generic.Validators.ListValidator
//    Checking: Chrysalis.Cbor.Types.Validators.ListValidator
//    Checking: Chrysalis.Cbor.Serialization.Validators.ListValidator
//    Performing broader search for: ListValidator
// Final validator detection result: None
// Extracted 9 properties
// Extracted 1 constructor parameters
// Extracted element type: int
// Final metadata extraction result:
//   Format: Object
//   ValidatorTypeName: None
//   HasValidator: False

// ----- END DETAILED DEBUG INFO -----

//     Property: Capacity
//       Type: int
//       Key: Capacity
//       IsCborNullable: False
//       IsPropertyNullable: False
//       Order: 
//       IsCollection: False
//       IsDictionary: False
//     Property: Count
//       Type: int
//       Key: Count
//       IsCborNullable: False
//       IsPropertyNullable: False
//       Order: 
//       IsCollection: False
//       IsDictionary: False
//     Property: this[]
//       Type: int
//       Key: this[]
//       IsCborNullable: False
//       IsPropertyNullable: False
//       Order: 
//       IsCollection: False
//       IsDictionary: False
//     Property: System.Collections.Generic.ICollection<T>.IsReadOnly
//       Type: bool
//       Key: System.Collections.Generic.ICollection<T>.IsReadOnly
//       IsCborNullable: False
//       IsPropertyNullable: False
//       Order: 
//       IsCollection: False
//       IsDictionary: False
//     Property: System.Collections.ICollection.IsSynchronized
//       Type: bool
//       Key: System.Collections.ICollection.IsSynchronized
//       IsCborNullable: False
//       IsPropertyNullable: False
//       Order: 
//       IsCollection: False
//       IsDictionary: False
//     Property: System.Collections.ICollection.SyncRoot
//       Type: object
//       Key: System.Collections.ICollection.SyncRoot
//       IsCborNullable: False
//       IsPropertyNullable: True
//       Order: 
//       IsCollection: False
//       IsDictionary: False
//     Property: System.Collections.IList.IsFixedSize
//       Type: bool
//       Key: System.Collections.IList.IsFixedSize
//       IsCborNullable: False
//       IsPropertyNullable: False
//       Order: 
//       IsCollection: False
//       IsDictionary: False
//     Property: System.Collections.IList.IsReadOnly
//       Type: bool
//       Key: System.Collections.IList.IsReadOnly
//       IsCborNullable: False
//       IsPropertyNullable: False
//       Order: 
//       IsCollection: False
//       IsDictionary: False
//     Property: System.Collections.IList.Item
//       Type: object
//       Key: System.Collections.IList.Item
//       IsCborNullable: False
//       IsPropertyNullable: True
//       Order: 
//       IsCollection: False
//       IsDictionary: False

// ======================================================
// Type: object
// ======================================================
//   Format: Object
//   Tag: 
//   IsIndefinite: False
//   Constructor: 
//   ValidatorTypeName: None
//   HasValidator: False
//   Properties: 0

// ----- DETAILED DEBUG INFO -----
// Extracting metadata for type: object
// Processing attributes for type: object
//   Attribute: System.Runtime.CompilerServices.NullableContextAttribute
// Detecting validator for: object
//    Searching for validator named: ObjectValidator
//    Checking: System.ObjectValidator
//    Checking: System.Validators.ObjectValidator
//    Checking: Chrysalis.Cbor.Types.Validators.ObjectValidator
//    Checking: Chrysalis.Cbor.Serialization.Validators.ObjectValidator
//    Performing broader search for: ObjectValidator
// Final validator detection result: None
// Extracted 0 properties
// Extracted 0 constructor parameters
// Final metadata extraction result:
//   Format: Object
//   ValidatorTypeName: None
//   HasValidator: False

// ----- END DETAILED DEBUG INFO -----


// ======================================================
// Type: global::System.Collections.Generic.IEnumerable<int>
// ======================================================
//   Format: Object
//   Tag: 
//   IsIndefinite: False
//   Constructor: 
//   ValidatorTypeName: None
//   HasValidator: False
//   Properties: 0

// ----- DETAILED DEBUG INFO -----
// Extracting metadata for type: System.Collections.Generic.IEnumerable<int>
// Processing attributes for type: System.Collections.Generic.IEnumerable<int>
// Detecting validator for: System.Collections.Generic.IEnumerable<int>
//    Interface: System.Collections.IEnumerable, IsValidator: False
//    Searching for validator named: IEnumerableValidator
//    Checking: System.Collections.Generic.IEnumerableValidator
//    Checking: System.Collections.Generic.Validators.IEnumerableValidator
//    Checking: Chrysalis.Cbor.Types.Validators.IEnumerableValidator
//    Checking: Chrysalis.Cbor.Serialization.Validators.IEnumerableValidator
//    Performing broader search for: IEnumerableValidator
// Final validator detection result: None
// Extracted 0 properties
// Extracted 0 constructor parameters
// Final metadata extraction result:
//   Format: Object
//   ValidatorTypeName: None
//   HasValidator: False

// ----- END DETAILED DEBUG INFO -----


