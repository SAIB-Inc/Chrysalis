using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using CoreAddress = Chrysalis.Codec.Types.Cardano.Core.Common.Address;
using Chrysalis.Codec.Types.Cardano.Core.Certificates;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Governance;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Plutus;
using Chrysalis.Codec.Types.Plutus.Address;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Test;

[CborSerializable, CborUnion(typeof(GenericDispatchPresent<>), typeof(GenericDispatchAbsent<>)), Union]
public readonly partial struct GenericDispatchChoice<T> : ICborType;

[CborSerializable, CborList, CborIndex(0)]
public readonly partial record struct GenericDispatchPresent<T> : ICborType
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial T Value { get; }
}

[CborSerializable, CborList, CborIndex(1)]
public readonly partial record struct GenericDispatchAbsent<T> : ICborType
{
    [CborOrder(0)] public partial int Tag { get; }
}

[CborSerializable, CborUnion]
public abstract partial record AbstractDispatchUnion<T> : CborRecord;

[CborSerializable, CborList]
public partial record AbstractDispatchCase<T>([CborOrder(0)] T Value) : AbstractDispatchUnion<T>;

public sealed class TypedDispatchTests
{
    private readonly record struct UnregisteredManualStruct(int Value) : ICborType
    {
        public ReadOnlyMemory<byte> Raw => default;
        public int ConstrIndex => 0;
        public bool IsIndefinite => false;

        public static UnregisteredManualStruct Read(ReadOnlyMemory<byte> data) => Read(data, out _);

        public static UnregisteredManualStruct Read(ReadOnlyMemory<byte> data, out int bytesConsumed)
        {
            CborReader reader = new(data.Span);
            int value = reader.ReadInt32();
            bytesConsumed = data.Length - reader.Buffer.Length;
            return new(value);
        }

        public static void Write(IBufferWriter<byte> output, UnregisteredManualStruct value) =>
            new CborWriter(output).WriteInt32(value.Value);
    }

    private readonly record struct UnregisteredPlainStruct(int Value);

    private sealed class UnregisteredReferenceClass(int value) : ICborType
    {
        public int Value { get; } = value;
        public ReadOnlyMemory<byte> Raw => default;
        public int ConstrIndex => 0;
        public bool IsIndefinite => false;

        public static UnregisteredReferenceClass Read(ReadOnlyMemory<byte> data, out int bytesConsumed)
        {
            CborReader reader = new(data.Span);
            int parsed = reader.ReadInt32();
            bytesConsumed = data.Length - reader.Buffer.Length;
            return new(parsed);
        }

        public static void Write(IBufferWriter<byte> output, UnregisteredReferenceClass value) =>
            new CborWriter(output).WriteInt32(value.Value);
    }

    private readonly record struct ThreadSafeManualStruct(int Value)
    {
        public static ThreadSafeManualStruct Read(ReadOnlyMemory<byte> data, out int bytesConsumed)
        {
            CborReader reader = new(data.Span);
            int value = reader.ReadInt32();
            bytesConsumed = data.Length - reader.Buffer.Length;
            return new(value);
        }

        public static void Write(IBufferWriter<byte> output, ThreadSafeManualStruct value) =>
            new CborWriter(output).WriteInt32(value.Value);
    }

    private sealed class ThreadSafeManualClass(int value)
    {
        public int Value { get; } = value;

        public static ThreadSafeManualClass Read(ReadOnlyMemory<byte> data, out int bytesConsumed)
        {
            CborReader reader = new(data.Span);
            int parsed = reader.ReadInt32();
            bytesConsumed = data.Length - reader.Buffer.Length;
            return new(parsed);
        }

        public static void Write(IBufferWriter<byte> output, ThreadSafeManualClass value) =>
            new CborWriter(output).WriteInt32(value.Value);
    }

    [Fact]
    public void ShippedAssembliesHaveZeroReflectionDispatchCalls()
    {
        string[] assemblyPaths =
        [
            typeof(GenericSerializationUtil).Assembly.Location,
            typeof(GlobalQuery).Assembly.Location
        ];

        List<string> violations = [];
        foreach (string path in assemblyPaths)
        {
            Assert.True(File.Exists(path), $"Expected assembly missing: {path}");
            using FileStream stream = File.OpenRead(path);
            using PEReader pe = new(stream);
            violations.AddRange(ScanAssembly(pe, Path.GetFileName(path)));
        }

        Assert.True(violations.Count == 0,
            $"Found {violations.Count} forbidden reflection calls in shipped IL:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void ReflectionGuardSelfTests()
    {
        MethodInfo typeGetMethod = typeof(Type).GetMethod(nameof(Type.GetMethod), [typeof(string)])!;
        MethodInfo methodInfoCreateDelegate = typeof(MethodInfo).GetMethod(nameof(MethodInfo.CreateDelegate), [typeof(Type)])!;
        MethodInfo methodInfoCreateDelegateGeneric = typeof(MethodInfo).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(static m => m.Name == nameof(MethodInfo.CreateDelegate) && m.IsGenericMethodDefinition && m.GetParameters().Length == 0)
            .MakeGenericMethod(typeof(Func<int>));
        MethodInfo activatorCreateInstanceGeneric = typeof(Activator).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(static m => m.Name == nameof(Activator.CreateInstance) && m.IsGenericMethodDefinition && m.GetParameters().Length == 0)
            .MakeGenericMethod(typeof(object));
        MethodInfo delegateCreateDelegate = typeof(Delegate).GetMethod(nameof(Delegate.CreateDelegate), [typeof(Type), typeof(MethodInfo)])!;
        MethodInfo runClassCtor = typeof(RuntimeHelpers).GetMethod(nameof(RuntimeHelpers.RunClassConstructor), [typeof(RuntimeTypeHandle)])!;
        MethodInfo getUninitialized = typeof(RuntimeHelpers).GetMethod(nameof(RuntimeHelpers.GetUninitializedObject), [typeof(Type)])!;
        MethodInfo getRuntimeMethod = typeof(RuntimeReflectionExtensions).GetMethod(nameof(RuntimeReflectionExtensions.GetRuntimeMethod), [typeof(Type), typeof(string), typeof(Type[])])!;
        MethodInfo expressionCompile = typeof(Expression<Func<int>>).GetMethod(nameof(Expression<>.Compile), Type.EmptyTypes)!;
        MethodInfo assemblyGetType = typeof(Assembly).GetMethod(nameof(Assembly.GetType), [typeof(string)])!;
        MethodInfo moduleGetMethod = typeof(Module).GetMethod(nameof(Module.GetMethod), [typeof(string)])!;
        MethodInfo methodInvokerCreate = typeof(MethodInvoker).GetMethod(nameof(MethodInvoker.Create), [typeof(MethodBase)])!;
        MethodInfo ctorInvokerCreate = typeof(ConstructorInvoker).GetMethod(nameof(ConstructorInvoker.Create), [typeof(ConstructorInfo)])!;
        MethodInfo getMethodFromHandle = typeof(MethodBase).GetMethod(nameof(MethodBase.GetMethodFromHandle), [typeof(RuntimeMethodHandle)])!;
        ConstructorInfo dynamicMethodCtor = typeof(DynamicMethod).GetConstructor([typeof(string), typeof(Type), typeof(Type[])])!;

        byte[] positivePe = EmitTestAssembly("PositiveReflectionProbe", il =>
        {
            il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Callvirt, typeGetMethod); il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Callvirt, methodInfoCreateDelegate); il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Callvirt, methodInfoCreateDelegateGeneric); il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Call, activatorCreateInstanceGeneric); il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Call, delegateCreateDelegate); il.Emit(OpCodes.Pop);
            _ = il.DeclareLocal(typeof(RuntimeTypeHandle));
            il.Emit(OpCodes.Ldloc_0); il.Emit(OpCodes.Call, runClassCtor);
            il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Call, getUninitialized); il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Call, getRuntimeMethod); il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldftn, runClassCtor); il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldvirtftn, typeGetMethod); il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Callvirt, expressionCompile); il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Callvirt, assemblyGetType); il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Callvirt, moduleGetMethod); il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Call, methodInvokerCreate); il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Call, ctorInvokerCreate); il.Emit(OpCodes.Pop);
            _ = il.DeclareLocal(typeof(RuntimeMethodHandle));
            il.Emit(OpCodes.Ldloc_1); il.Emit(OpCodes.Call, getMethodFromHandle); il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Newobj, dynamicMethodCtor); il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
        });

        List<string> positiveHits = ScanPeBytes(positivePe, "PositiveReflectionProbe.dll");
        Assert.Equal(17, positiveHits.Count);
        Assert.Contains(positiveHits, static h => h.Contains("System.Type::GetMethod", StringComparison.Ordinal));
        Assert.Contains(positiveHits, static h => h.Contains("MethodInfo::CreateDelegate", StringComparison.Ordinal));
        Assert.Contains(positiveHits, static h => h.Contains("Activator::CreateInstance", StringComparison.Ordinal));
        Assert.Contains(positiveHits, static h => h.Contains("Delegate::CreateDelegate", StringComparison.Ordinal));
        Assert.Contains(positiveHits, static h => h.Contains("RuntimeHelpers::RunClassConstructor", StringComparison.Ordinal));
        Assert.Contains(positiveHits, static h => h.Contains("RuntimeHelpers::GetUninitializedObject", StringComparison.Ordinal));
        Assert.Contains(positiveHits, static h => h.Contains("RuntimeReflectionExtensions::GetRuntimeMethod", StringComparison.Ordinal));
        Assert.Contains(positiveHits, static h => h.Contains("ldftn", StringComparison.Ordinal));
        Assert.Contains(positiveHits, static h => h.Contains("ldvirtftn", StringComparison.Ordinal));
        Assert.Contains(positiveHits, static h => h.Contains("System.Linq.Expressions.Expression", StringComparison.Ordinal));
        Assert.Contains(positiveHits, static h => h.Contains("Assembly::GetType", StringComparison.Ordinal));
        Assert.Contains(positiveHits, static h => h.Contains("Module::GetMethod", StringComparison.Ordinal));
        Assert.Contains(positiveHits, static h => h.Contains("MethodInvoker::Create", StringComparison.Ordinal));
        Assert.Contains(positiveHits, static h => h.Contains("ConstructorInvoker::Create", StringComparison.Ordinal));
        Assert.Contains(positiveHits, static h => h.Contains("MethodBase::GetMethodFromHandle", StringComparison.Ordinal));
        Assert.Contains(positiveHits, static h => h.Contains("newobj System.Reflection.Emit.DynamicMethod::.ctor", StringComparison.Ordinal));

        MethodInfo runModuleCtor = typeof(RuntimeHelpers).GetMethod(nameof(RuntimeHelpers.RunModuleConstructor), [typeof(ModuleHandle)])!;
        MethodInfo getTypeCode = typeof(Type).GetMethod(nameof(Type.GetTypeCode), [typeof(Type)])!;
        MethodInfo getUnderlying = typeof(Nullable).GetMethod(nameof(Nullable.GetUnderlyingType), [typeof(Type)])!;
        MethodInfo genericTypeArgs = typeof(Type).GetProperty(nameof(Type.GenericTypeArguments))!.GetGetMethod()!;
        MethodInfo typeGetModule = typeof(Type).GetProperty(nameof(Type.Module))!.GetGetMethod()!;
        MethodInfo moduleGetHandle = typeof(Module).GetProperty(nameof(Module.ModuleHandle))!.GetGetMethod()!;
        MethodInfo objGetType = typeof(object).GetMethod(nameof(object.GetType))!;
        MethodInfo cacheRegister = typeof(CborCodecCache).GetMethod(nameof(CborCodecCache.Register))!.MakeGenericMethod(typeof(UnregisteredReferenceClass));

        byte[] negativePe = EmitTestAssembly("NegativeAllowedProbe", il =>
        {
            _ = il.DeclareLocal(typeof(ModuleHandle));
            il.Emit(OpCodes.Ldloc_0); il.Emit(OpCodes.Call, runModuleCtor);
            il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Call, getTypeCode); il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Call, getUnderlying); il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Callvirt, genericTypeArgs); il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Callvirt, typeGetModule); il.Emit(OpCodes.Callvirt, moduleGetHandle); il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Callvirt, objGetType); il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Call, cacheRegister);
            il.Emit(OpCodes.Ret);
        });

        List<string> negativeHits = ScanPeBytes(negativePe, "NegativeAllowedProbe.dll");
        Assert.Empty(negativeHits);
    }

    [Fact]
    public void UnregisteredTypesAndNullContractsAreEnforcedWithoutReflection()
    {
        byte[] intBytes = [0x01];
        ArrayBufferWriter<byte> output = new();

        Type unregStructType = typeof(UnregisteredManualStruct);
        Type unregNullableStructType = typeof(UnregisteredManualStruct?);
        Type unregClassType = typeof(UnregisteredReferenceClass);
        Type blockType = typeof(ConwayBlock);
        static void AssertUnregisteredRead(Action action)
        {
            CborDecodeException err = Assert.Throws<CborDecodeException>(action);
            Assert.Equal("registered CBOR type", err.Expected);
            Assert.Equal("unregistered type", err.Actual);
            Assert.Null(err.InnerException);
        }

        static void AssertUnregisteredWrite(Action action)
        {
            CborValidationException err = Assert.Throws<CborValidationException>(action);
            Assert.Equal("registered CBOR type", err.Expected);
            Assert.Equal("unregistered type", err.Actual);
            Assert.Null(err.InnerException);
        }

        AssertUnregisteredRead(() => _ = CborSerializer.Deserialize<UnregisteredManualStruct>(intBytes));
        AssertUnregisteredRead(() => _ = CborSerializer.Deserialize<UnregisteredManualStruct?>(intBytes));
        AssertUnregisteredRead(() => _ = CborSerializer.Deserialize<UnregisteredPlainStruct>(intBytes));
        AssertUnregisteredRead(() => _ = CborSerializer.Deserialize<DateTime>(intBytes));
        AssertUnregisteredRead(() => _ = CborSerializer.Deserialize<UnregisteredReferenceClass>(intBytes));
        AssertUnregisteredRead(() => _ = GenericSerializationUtil.Read<UnregisteredManualStruct>(intBytes));
        AssertUnregisteredRead(() => _ = GenericSerializationUtil.ReadAnyWithConsumed<UnregisteredManualStruct?>(intBytes, out _));
        AssertUnregisteredRead(() => _ = GenericSerializationUtil.Read(intBytes, unregStructType));
        AssertUnregisteredRead(() => _ = GenericSerializationUtil.Read(intBytes, unregNullableStructType));
        AssertUnregisteredRead(() => _ = GenericSerializationUtil.ReadAnyWithConsumed(intBytes, unregClassType, out _));

        AssertUnregisteredWrite(() => _ = CborSerializer.Serialize(new UnregisteredManualStruct(1)));
        AssertUnregisteredWrite(() => _ = CborSerializer.Serialize(new UnregisteredReferenceClass(1)));
        AssertUnregisteredWrite(() => GenericSerializationUtil.Write(output, new UnregisteredManualStruct(1)));
        AssertUnregisteredWrite(() => GenericSerializationUtil.Write<UnregisteredManualStruct?>(output, null));
        AssertUnregisteredWrite(() => GenericSerializationUtil.Write(output, new UnregisteredReferenceClass(1)));

        CborDecodeException listErr = Assert.Throws<CborDecodeException>(() =>
        {
            foreach (UnregisteredManualStruct _ in CborList<UnregisteredManualStruct>.Read(new byte[] { 0x81, 0x01 })) { }
        });
        Assert.Equal("registered CBOR type", listErr.Expected);
        Assert.Equal("unregistered type", listErr.Actual);

        CborDecodeException mapValErr = Assert.Throws<CborDecodeException>(() =>
            _ = CborMap<int, UnregisteredManualStruct>.Read(Convert.FromHexString("A10101"))[1]);
        Assert.Equal("registered CBOR type", mapValErr.Expected);
        Assert.Equal("unregistered type", mapValErr.Actual);

        CborDecodeException mapKeyErr = Assert.Throws<CborDecodeException>(() =>
            _ = CborMap<UnregisteredManualStruct, int>.Read(Convert.FromHexString("A10101")));
        Assert.Equal("registered CBOR type", mapKeyErr.Expected);
        Assert.Equal("unregistered type", mapKeyErr.Actual);

        _ = Assert.Throws<CborDecodeException>(() => GenericSerializationUtil.ReadPrimitive(blockType, new byte[] { 0x80 }));
        _ = Assert.Throws<CborDecodeException>(() => GenericSerializationUtil.ReadPrimitive<ConwayBlock>(new byte[] { 0x80 }));

        _ = Assert.Throws<ArgumentNullException>(() => GenericSerializationUtil.Write<string>(output, null!));
        _ = Assert.Throws<ArgumentNullException>(() => GenericSerializationUtil.Write<byte[]>(output, null!));
        _ = Assert.Throws<ArgumentNullException>(() => GenericSerializationUtil.Write<CborAny>(output, null!));
        _ = Assert.Throws<ArgumentNullException>(() => GenericSerializationUtil.Write<int?>(output, null));
        _ = Assert.Throws<CborValidationException>(() => GenericSerializationUtil.Write<Withdrawals?>(output, null));
        _ = Assert.Throws<CborValidationException>(() => GenericSerializationUtil.WritePrimitive<DRep>(output, default(DRep)));

        byte[] nullWire = [0xF6];
        _ = Assert.Throws<CborDecodeException>(() => CborCodecCache.GetReader<int?>()!(nullWire, out _));
        _ = Assert.Throws<CborDecodeException>(() => CborCodecCache.GetReader<Withdrawals?>()!(nullWire, out _));
        _ = Assert.Throws<CborDecodeException>(() => CborCodecCache.GetReader<TransactionInput?>()!(nullWire, out _));

        byte[] listNull = Convert.FromHexString("81F6");
        _ = Assert.Throws<CborDecodeException>(() => { foreach (TransactionInput? _ in CborList<TransactionInput?>.Read(listNull)) { } });
        _ = Assert.Throws<CborDecodeException>(() => { foreach (decimal _ in CborList<decimal>.Read(listNull)) { } });
        _ = Assert.Throws<CborDecodeException>(() => { foreach (decimal? _ in CborList<decimal?>.Read(listNull)) { } });
        _ = Assert.Throws<CborDecodeException>(() => { foreach (CborLabel _ in CborList<CborLabel>.Read(listNull)) { } });
        _ = Assert.Throws<CborDecodeException>(() => _ = CborList<CborAny>.Read(new byte[] { 0x81, 0xFF }));
        _ = Assert.Throws<CborDecodeException>(() => _ = CborSerializer.Deserialize<CborAny>(new byte[] { 0xFF }));
        _ = Assert.Throws<CborDecodeException>(() => { foreach (CborAny _ in new CborListEnumerable<CborAny>(new byte[] { 0x81, 0x42 })) { } });
    }

    [Fact]
    public void GenericUnionsGenericCollectionsAndAddressRoundTripThroughCache()
    {
        GenericDispatchChoice<int> intUnion = GenericDispatchPresent<int>.Create(0, 77);
        GenericDispatchChoice<int> intRound = CborSerializer.Deserialize<GenericDispatchChoice<int>>(CborSerializer.Serialize(intUnion));
        Assert.True(intRound.TryGetValue(out GenericDispatchPresent<int> presentInt) && presentInt.Value == 77);

        GenericDispatchChoice<string> strUnion = GenericDispatchAbsent<string>.Create(1);
        GenericDispatchChoice<string> strRound = CborSerializer.Deserialize<GenericDispatchChoice<string>>(CborSerializer.Serialize(strUnion));
        Assert.True(strRound.TryGetValue(out GenericDispatchAbsent<string> absentStr) && absentStr.Tag == 1);

        DRep drep = Abstain.Create(2);
        GenericDispatchChoice<DRep> drepUnion = GenericDispatchPresent<DRep>.Create(0, drep);
        GenericDispatchChoice<DRep> drepRound = CborSerializer.Deserialize<GenericDispatchChoice<DRep>>(CborSerializer.Serialize(drepUnion));
        Assert.True(drepRound.TryGetValue(out GenericDispatchPresent<DRep> presentDrep) && presentDrep.Value.Raw.Span.SequenceEqual(drep.Raw.Span));

        ICborOption<IPlutusBigInt> optSome = Some<IPlutusBigInt>.Create(PlutusInt.Create(15));
        ICborOption<IPlutusBigInt> optNone = None<IPlutusBigInt>.Create();
        _ = Assert.IsType<Some<IPlutusBigInt>>(CborSerializer.Deserialize<ICborOption<IPlutusBigInt>>(CborSerializer.Serialize(optSome)));
        _ = Assert.IsType<None<IPlutusBigInt>>(CborSerializer.Deserialize<ICborOption<IPlutusBigInt>>(CborSerializer.Serialize(optNone)));

        IReferenced<ICredential> referenced = Inline<ICredential>.Create(VerificationKey.Create(new byte[28]));
        _ = Assert.IsType<Inline<ICredential>>(CborSerializer.Deserialize<IReferenced<ICredential>>(CborSerializer.Serialize(referenced)));

        Cip68<PlutusInt> cip68 = new(PlutusInt.Create(1), 1, PlutusInt.Create(99));
        Cip68<PlutusInt> cip68Round = CborSerializer.Deserialize<Cip68<PlutusInt>>(CborSerializer.Serialize(cip68));
        Assert.Equal(99, Assert.IsType<PlutusInt>(cip68Round.Extra!).Value);

        CborList<TransactionInput> txInputs = CborList.Create([TransactionInput.Create(new byte[32], 3)]);
        byte[] txInputBytes = CborSerializer.Serialize(txInputs);
        CborList<TransactionInput>? optList = CborSerializer.Deserialize<CborList<TransactionInput>?>(txInputBytes);
        Assert.True(optList.HasValue && optList.Value.Count == 1);
        Assert.Equal(txInputBytes, CborSerializer.Serialize(optList.Value));
        Type txListType = typeof(CborList<TransactionInput>);
        Type txNullableListType = typeof(CborList<TransactionInput>?);
        CborList<TransactionInput> boxedList = Assert.IsType<CborList<TransactionInput>>(
            GenericSerializationUtil.Read(txInputBytes, txListType));
        Assert.Equal(3UL, Assert.Single(boxedList).Index);
        _ = Assert.IsType<CborList<TransactionInput>>(GenericSerializationUtil.Read(txInputBytes, txNullableListType));

        CborList<int> intList = CborSerializer.Deserialize<CborList<int>>(CborSerializer.Serialize(CborList.Create([1, 2, 3])));
        Assert.Equal([1, 2, 3], [.. intList]);
        CborMap<int, string> builtMap = [new(1, "one")];
        CborMap<int, string> map = CborSerializer.Deserialize<CborMap<int, string>>(CborSerializer.Serialize(builtMap));
        Assert.Equal("one", map[1]);

        Assert.NotNull(CborCodecCache.GetReader<CoreAddress>());
        Assert.NotNull(CborCodecCache.GetWriter<CoreAddress>());
        Assert.NotNull(CborCodecCache.GetReader<CoreAddress?>());
        CoreAddress addr = CoreAddress.Create(new byte[] { 0x60, 0xAA, 0xBB });
        Assert.Equal(addr.Value.ToArray(), CborSerializer.Deserialize<CoreAddress>(CborSerializer.Serialize(addr)).Value.ToArray());
        ArrayBufferWriter<byte> addrBuf = new();
        GenericSerializationUtil.Write<CoreAddress?>(addrBuf, addr);
        Assert.Equal(addr.Value.ToArray(), CborSerializer.Deserialize<CoreAddress?>(addrBuf.WrittenMemory)!.Value.Value.ToArray());

        AbstractDispatchUnion<int> abs = AbstractDispatchCase<int>.Create(99);
        Assert.False(typeof(ICborCodecRegistrar).IsAssignableFrom(typeof(AbstractDispatchUnion<int>)));
        Assert.Equal(99, Assert.IsType<AbstractDispatchCase<int>>(
            CborSerializer.Deserialize<AbstractDispatchUnion<int>>(CborSerializer.Serialize(abs))).Value);
    }

    [Fact]
    public void CrossAssemblyColdStartRegistersWithoutTouchingTypesFirst()
    {
        const string coldSource = """
            global using System;
            global using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            using Chrysalis.Codec.Types;
            namespace ColdExternalConsumer;

            [CborSerializable, CborList]
            public partial record ExternalRecord([CborOrder(0)] int Value) : CborRecord;

            [CborSerializable, CborList]
            public readonly partial record struct ExternalRecordStruct : ICborType
            {
                [CborOrder(0)] public partial int Value { get; }
            }

            [CborSerializable, CborList]
            public partial record ExternalWrap<T>([CborOrder(0)] T Value) : CborRecord;

            [CborSerializable, CborUnion(typeof(ExternalOptSome<>), typeof(ExternalOptNone<>)), Union]
            public readonly partial struct ExternalOpt<T> : ICborType;

            [CborSerializable, CborList, CborIndex(0)]
            public readonly partial record struct ExternalOptSome<T> : ICborType
            {
                [CborOrder(0)] public partial int Tag { get; }
                [CborOrder(1)] public partial T Value { get; }
            }

            [CborSerializable, CborList, CborIndex(1)]
            public readonly partial record struct ExternalOptNone<T> : ICborType
            {
                [CborOrder(0)] public partial int Tag { get; }
            }

            public static class ClosedRefs
            {
                public static Type[] Used() =>
                [
                    typeof(ExternalWrap<int>),
                    typeof(ICborOption<ExternalRecord>),
                    typeof(CborList<ExternalRecordStruct>)
                ];
            }
            """;

        CSharpCompilation coldComp = GeneratorIsolationTests.CreateCompilation("ColdExternalConsumer", coldSource);
        _ = GeneratorIsolationTests.CreateDriver().RunGeneratorsAndUpdateCompilation(coldComp, out Compilation coldGenerated, out _);
        byte[] coldBytes = EmitBytes(coldGenerated);

        const string noCodeGenSource = """
            using System;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Types;
            using Chrysalis.Codec.Types.Cardano.Core.Governance;
            using Chrysalis.Codec.Types.Cardano.Core.Transaction;
            using ColdExternalConsumer;

            namespace NoCodeGenConsumer;

            public static class Runner
            {
                public static void VerifyColdOption(ReadOnlyMemory<byte> optionBytes)
                {
                    ICborOption<ExternalRecord> opt = CborSerializer.Deserialize<ICborOption<ExternalRecord>>(optionBytes);
                    if (opt is not Some<ExternalRecord> some || some.Value.Value != 42)
                    {
                        throw new InvalidOperationException("Cold ICborOption<ExternalRecord> failed.");
                    }
                }

                public static void Verify()
                {
                    byte[] txListBytes = CborSerializer.Serialize(CborList.Create([TransactionInput.Create(new byte[32], 5)]));
                    CborList<TransactionInput>? optList = CborSerializer.Deserialize<CborList<TransactionInput>?>(txListBytes);
                    if (optList is null || optList.Value.Count != 1) { throw new InvalidOperationException("Nullable CborList<TransactionInput> failed."); }
                    object? boxed = GenericSerializationUtil.Read(txListBytes, typeof(CborList<TransactionInput>));
                    if (boxed is not CborList<TransactionInput> listObj || listObj.Count != 1) { throw new InvalidOperationException("Boxed CborList<TransactionInput> failed."); }

                    byte[] drepListBytes = CborSerializer.Serialize(CborList.Create<DRep>([Abstain.Create(2)]));
                    CborList<DRep> dreps = CborSerializer.Deserialize<CborList<DRep>>(drepListBytes);
                    if (dreps.Count != 1) { throw new InvalidOperationException("CborList<DRep> failed without CodeGen."); }

                    byte[] extOptBytes = CborSerializer.Serialize<ExternalOpt<int>>(ExternalOptSome<int>.Create(0, 123));
                    ExternalOpt<int> decodedOpt = CborSerializer.Deserialize<ExternalOpt<int>>(extOptBytes);
                    if (!decodedOpt.TryGetValue(out ExternalOptSome<int> someVal) || someVal.Value != 123)
                    {
                        throw new InvalidOperationException("Generic official [Union] struct failed without CodeGen.");
                    }

                    byte[] wrapBytes = CborSerializer.Serialize(new ExternalWrap<string>("cold-ok"));
                    if (wrapBytes.Length == 0 || CborSerializer.GetEncodedLength(new ExternalWrap<long>(42)) <= 0)
                    {
                        throw new InvalidOperationException("Open-generic write/length failed without CodeGen.");
                    }
                }
            }
            """;

        CSharpCompilation noCodeGenComp = CSharpCompilation.Create(
            "NoCodeGenConsumer",
            [CSharpSyntaxTree.ParseText(noCodeGenSource, GeneratorIsolationTests.ParseOptions)],
            [.. GeneratorIsolationTests.References, MetadataReference.CreateFromImage(coldBytes)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        byte[] noCodeGenBytes = EmitBytes(noCodeGenComp);

        const string brokenInitSource = """
            using System;
            using System.Buffers;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;

            namespace BrokenInitConsumer;

            public static class ModuleInit
            {
                [ModuleInitializer]
                internal static void Init() => throw new InvalidOperationException("boom");
            }

            public readonly struct BrokenInitType : ICborType
            {
                public ReadOnlyMemory<byte> Raw => default;
                public int ConstrIndex => 0;
                public bool IsIndefinite => false;
                public static BrokenInitType Read(ReadOnlyMemory<byte> data, out int bytesConsumed) { bytesConsumed = 0; return default; }
                public static void Write(IBufferWriter<byte> output, BrokenInitType value) { }
            }
            """;

        CSharpCompilation brokenInitComp = GeneratorIsolationTests.CreateCompilation("BrokenInitConsumer", brokenInitSource);
        byte[] brokenBytes = EmitBytes(brokenInitComp);

        const string brokenCallerSource = """
            using System;
            using Chrysalis.Codec.Serialization;
            using BrokenInitConsumer;

            namespace BrokenInitCaller;

            public static class Caller
            {
                public static void CallDeserialize(ReadOnlyMemory<byte> data) => _ = CborSerializer.Deserialize<BrokenInitType>(data);
                public static void CallReadGeneric(ReadOnlyMemory<byte> data) => _ = GenericSerializationUtil.Read<BrokenInitType>(data);
                public static void CallReadByType(ReadOnlyMemory<byte> data) => _ = GenericSerializationUtil.Read(data, typeof(BrokenInitType));
                public static void CallSerialize() => _ = CborSerializer.Serialize<BrokenInitType>(default);
            }
            """;

        CSharpCompilation brokenCallerComp = CSharpCompilation.Create(
            "BrokenInitCaller",
            [CSharpSyntaxTree.ParseText(brokenCallerSource, GeneratorIsolationTests.ParseOptions)],
            [.. GeneratorIsolationTests.References, MetadataReference.CreateFromImage(brokenBytes)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        byte[] brokenCallerBytes = EmitBytes(brokenCallerComp);

        AssemblyLoadContext coldBoxedAlc = new("ColdBoxedAlc", isCollectible: true);
        try
        {
            using MemoryStream coldMs = new(coldBytes);
            Assembly coldAsm = coldBoxedAlc.LoadFromStream(coldMs);
            Type recType = coldAsm.GetType("ColdExternalConsumer.ExternalRecord", throwOnError: true)!;
            object? boxedRec = GenericSerializationUtil.Read(new byte[] { 0x81, 0x18, 0x2A }, recType);
            Assert.Equal(42, (int)recType.GetProperty("Value")!.GetValue(boxedRec)!);
        }
        finally
        {
            coldBoxedAlc.Unload();
        }

        AssemblyLoadContext coldOptAlc = new("ColdOptAlc", isCollectible: true);
        try
        {
            using MemoryStream coldMs = new(coldBytes);
            using MemoryStream noCodeGenMs = new(noCodeGenBytes);
            _ = coldOptAlc.LoadFromStream(coldMs);
            Assembly noCodeGenAsm = coldOptAlc.LoadFromStream(noCodeGenMs);
            ReadOnlyMemory<byte> someExternal42 = Convert.FromHexString("D8799F81182AFF");
            _ = noCodeGenAsm.GetType("NoCodeGenConsumer.Runner", throwOnError: true)!
                .GetMethod("VerifyColdOption", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [someExternal42]);
        }
        finally
        {
            coldOptAlc.Unload();
        }

        AssemblyLoadContext coldAlc = new("ColdExternalAlc", isCollectible: true);
        try
        {
            using MemoryStream coldMs = new(coldBytes);
            Assembly coldAsm = coldAlc.LoadFromStream(coldMs);
            Type recType = coldAsm.GetType("ColdExternalConsumer.ExternalRecord", throwOnError: true)!;
            Type structType = coldAsm.GetType("ColdExternalConsumer.ExternalRecordStruct", throwOnError: true)!;
            Type nullableStructType = typeof(Nullable<>).MakeGenericType(structType);
            Type wrapType = coldAsm.GetType("ColdExternalConsumer.ExternalWrap`1", throwOnError: true)!.MakeGenericType(typeof(int));

            ReadOnlyMemory<byte> listOne42 = new byte[] { 0x81, 0x18, 0x2A };
            MethodInfo deserialize = typeof(CborSerializer).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(static m => m.Name == nameof(CborSerializer.Deserialize) && m.GetParameters().Length == 1);
            object? decodedRec = deserialize.MakeGenericMethod(recType).Invoke(null, [listOne42]);
            Assert.Equal(42, (int)recType.GetProperty("Value")!.GetValue(decodedRec)!);

            object? decodedNullableStruct = deserialize.MakeGenericMethod(nullableStructType).Invoke(null, [listOne42]);
            Assert.Equal(42, (int)structType.GetProperty("Value")!.GetValue(decodedNullableStruct)!);

            object? decodedWrap = deserialize.MakeGenericMethod(wrapType).Invoke(null, [listOne42]);
            Assert.Equal(42, (int)wrapType.GetProperty("Value")!.GetValue(decodedWrap)!);

            object? boxedRec = GenericSerializationUtil.Read(listOne42, recType);
            Assert.Equal(42, (int)recType.GetProperty("Value")!.GetValue(boxedRec)!);
        }
        finally
        {
            coldAlc.Unload();
        }

        AssemblyLoadContext noCodeGenAlc = new("NoCodeGenAlc", isCollectible: true);
        try
        {
            using MemoryStream coldMs = new(coldBytes);
            using MemoryStream noCodeGenMs = new(noCodeGenBytes);
            _ = noCodeGenAlc.LoadFromStream(coldMs);
            Assembly noCodeGenAsm = noCodeGenAlc.LoadFromStream(noCodeGenMs);
            _ = noCodeGenAsm.GetType("NoCodeGenConsumer.Runner", throwOnError: true)!
                .GetMethod("Verify", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, null);
        }
        finally
        {
            noCodeGenAlc.Unload();
        }

        static void AssertBrokenMethod(byte[] modBytes, byte[] callerBytes, string methodName, object?[]? args)
        {
            AssemblyLoadContext alc = new("BrokenInitAlc_" + methodName, isCollectible: true);
            try
            {
                using MemoryStream brokenMs = new(modBytes);
                using MemoryStream callerMs = new(callerBytes);
                _ = alc.LoadFromStream(brokenMs);
                Type callerType = alc.LoadFromStream(callerMs).GetType("BrokenInitCaller.Caller", throwOnError: true)!;
                TargetInvocationException ex = Assert.Throws<TargetInvocationException>(() => callerType.GetMethod(methodName)!.Invoke(null, args));
                TypeInitializationException tie = Assert.IsType<TypeInitializationException>(ex.InnerException);
                _ = Assert.IsType<InvalidOperationException>(tie.InnerException);
            }
            finally
            {
                alc.Unload();
            }
        }

        ReadOnlyMemory<byte> dummy = new byte[] { 0x00 };
        AssertBrokenMethod(brokenBytes, brokenCallerBytes, "CallDeserialize", [dummy]);
        AssertBrokenMethod(brokenBytes, brokenCallerBytes, "CallReadGeneric", [dummy]);
        AssertBrokenMethod(brokenBytes, brokenCallerBytes, "CallReadByType", [dummy]);
        AssertBrokenMethod(brokenBytes, brokenCallerBytes, "CallSerialize", null);
    }

    [Fact]
    [Trait("Category", "Allocation")]
    public void TypedDispatchAndOpenGenericPrimitiveLengthAllocateZeroBytes()
    {
        LengthBox<long> box = new(42);
        long sink = 0;
        Assert.Equal(0, MeasureZero(() => _ = CborSerializer.GetEncodedLength(box), () => sink += CborSerializer.GetEncodedLength(box)));

        byte[] drepWire = CborSerializer.Serialize<DRep>(Abstain.Create(2));
        Assert.Equal(0, MeasureZero(() => _ = CborSerializer.Deserialize<DRep>(drepWire), () => sink += CborSerializer.Deserialize<DRep>(drepWire).Raw.Length));

        byte[] txInputWire = CborSerializer.Serialize(TransactionInput.Create(new byte[32], 1));
        Assert.Equal(0, MeasureZero(() => _ = CborSerializer.Deserialize<TransactionInput>(txInputWire), () => sink += CborSerializer.Deserialize<TransactionInput>(txInputWire).Raw.Length));

        byte[] ulongWire = [0x18, 0x2A];
        Assert.Equal(0, MeasureZero(() => _ = CborSerializer.Deserialize<ulong>(ulongWire), () => sink += (long)CborSerializer.Deserialize<ulong>(ulongWire)));

        byte[] relayListWire = CborSerializer.Serialize(CborList.Create<Relay>([SingleHostName.Create(1, "example.com", 3001)]));
        CborList<Relay> relayList = CborList<Relay>.Read(relayListWire);
        Assert.Equal(0, MeasureZero(
            () => { foreach (Relay r in relayList) { sink += r.Raw.Length; } },
            () => { foreach (Relay r in relayList) { sink += r.Raw.Length; } }));

        CborList<int> intList = CborList<int>.Read(Convert.FromHexString("83010203"));
        Assert.Equal(0, MeasureZero(
            () => { foreach (int n in intList) { sink += n; } },
            () => { foreach (int n in intList) { sink += n; } }));

        CborMap<int, ulong> map = CborMap<int, ulong>.Read(Convert.FromHexString("A2010A0214"));
        Assert.Equal(0, MeasureZero(
            () => { foreach (KeyValuePair<int, ulong> kv in map) { sink += kv.Key + (long)kv.Value; } },
            () => { foreach (KeyValuePair<int, ulong> kv in map) { sink += kv.Key + (long)kv.Value; } }));

        Assert.True(sink > 0);
    }

    [Fact]
    public async Task CborCodecCacheThreadSafetySupportsConcurrentRegistration()
    {
        const int workers = 8;
        using Barrier barrier = new(workers);
        Task[] tasks = new Task[workers];
        for (int i = 0; i < workers; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                barrier.SignalAndWait();
                CborCodecCache.Register(ThreadSafeManualClass.Read, ThreadSafeManualClass.Write);
                CborCodecCache.RegisterStruct(ThreadSafeManualStruct.Read, ThreadSafeManualStruct.Write);
                _ = CborCodecCache.GetReader<ThreadSafeManualClass>();
                _ = CborCodecCache.GetWriter<ThreadSafeManualStruct?>();
            });
        }

        await Task.WhenAll(tasks);
        Assert.NotNull(CborCodecCache.GetReader<ThreadSafeManualClass>());
        Assert.NotNull(CborCodecCache.GetWriter<ThreadSafeManualClass>());
        Assert.NotNull(CborCodecCache.GetReader<ThreadSafeManualStruct>());
        Assert.NotNull(CborCodecCache.GetWriter<ThreadSafeManualStruct?>());
    }

    private static byte[] EmitBytes(Compilation compilation)
    {
        using MemoryStream ms = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emit = compilation.Emit(ms);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return ms.ToArray();
    }

    private static long MeasureZero(Action warmUp, Action measure) =>
        AllocationAssert.Measure(() =>
        {
            for (int i = 0; i < 64; i++)
            {
                warmUp();
            }
        }, measure);

    private static byte[] EmitTestAssembly(string assemblyName, Action<ILGenerator> emitBody)
    {
        PersistedAssemblyBuilder ab = new(new AssemblyName(assemblyName), typeof(object).Assembly);
        ModuleBuilder mb = ab.DefineDynamicModule("MainModule");
        TypeBuilder tb = mb.DefineType("Probe.Synthetic", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract);
        MethodBuilder method = tb.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        emitBody(method.GetILGenerator());
        _ = tb.CreateType();
        using MemoryStream ms = new();
        ab.Save(ms);
        return ms.ToArray();
    }

    private static List<string> ScanPeBytes(byte[] peBytes, string label)
    {
        using MemoryStream ms = new(peBytes, writable: false);
        using PEReader pe = new(ms);
        return ScanAssembly(pe, label);
    }

    private static List<string> ScanAssembly(PEReader pe, string assemblyLabel)
    {
        MetadataReader mr = pe.GetMetadataReader();
        string thisAsm = mr.GetString(mr.GetAssemblyDefinition().Name);
        SignatureTypeProvider provider = new();
        List<string> violations = [];

        foreach (MethodDefinitionHandle mdHandle in mr.MethodDefinitions)
        {
            MethodDefinition md = mr.GetMethodDefinition(mdHandle);
            if (md.RelativeVirtualAddress == 0) { continue; }
            MethodBodyBlock body = pe.GetMethodBody(md.RelativeVirtualAddress);
            byte[]? ilBytes = body.GetILBytes();
            if (ilBytes is null || ilBytes.Length == 0) { continue; }

            string declaringName = GetTypeDefFullName(mr, md.GetDeclaringType());
            string methodName = mr.GetString(md.Name);
            string ownerLabel = $"{assemblyLabel}: {declaringName}.{methodName}";
            GenericContext gctx = new(thisAsm, declaringName, methodName);
            ReadOnlySpan<byte> il = ilBytes;
            int ip = 0;

            while (ip < il.Length)
            {
                int instrOffset = ip;
                (OpCode op, int operandStart, int operandBytes, int token) = DecodeInstruction(il, ip, ownerLabel);
                int nextIp = operandStart + operandBytes;

                if (op.Value == OpCodes.Call.Value
                    || op.Value == OpCodes.Callvirt.Value
                    || op.Value == OpCodes.Ldftn.Value
                    || op.Value == OpCodes.Ldvirtftn.Value
                    || op.Value == OpCodes.Newobj.Value)
                {
                    if (TryResolveMethodTarget(mr, MetadataTokens.EntityHandle(token), provider, gctx,
                        out string targetType, out string targetMethod)
                        && IsBannedReflectionCall(targetType, targetMethod))
                    {
                        violations.Add($"{ownerLabel} IL_{instrOffset:X4}: {op.Name} {targetType}::{targetMethod}");
                    }
                }

                ip = nextIp;
            }
        }

        return violations;
    }

    private static bool IsBannedReflectionCall(string typeName, string methodName)
    {
        if (typeName.StartsWith("System.Linq.Expressions.", StringComparison.Ordinal)
            || typeName.StartsWith("System.Reflection.Emit.", StringComparison.Ordinal)
            || typeName.StartsWith("Microsoft.CSharp.RuntimeBinder.", StringComparison.Ordinal)
            || typeName == "System.Activator"
            || typeName.StartsWith("System.Activator.", StringComparison.Ordinal))
        {
            return true;
        }

        return typeName switch
        {
            "System.Type" => methodName is "GetMethod" or "GetMethods" or "GetProperty" or "GetProperties"
                or "GetField" or "GetFields" or "GetConstructor" or "GetConstructors"
                or "GetMember" or "GetMembers" or "InvokeMember" or "GetType"
                or "MakeGenericType" or "MakeByRefType"
                or "GetNestedType" or "GetNestedTypes" or "FindMembers",
            "System.Reflection.MethodInfo" => methodName is "MakeGenericMethod" or "CreateDelegate",
            "System.Reflection.MethodBase" => methodName is "Invoke" or "GetMethodFromHandle",
            "System.Reflection.ConstructorInfo" => methodName == "Invoke",
            "System.Reflection.PropertyInfo" or "System.Reflection.FieldInfo" => methodName is "GetValue" or "SetValue",
            "System.Reflection.RuntimeReflectionExtensions"
                or "System.Reflection.IntrospectionExtensions"
                or "System.Reflection.TypeInfo"
                or "System.Reflection.MethodInvoker"
                or "System.Reflection.ConstructorInvoker" => true,
            "System.Reflection.Assembly" => methodName is "CreateInstance" or "GetType" or "GetTypes" or "GetExportedTypes",
            "System.Reflection.Module" => methodName is "GetType" or "GetTypes" or "GetMethod" or "GetMethods"
                or "GetField" or "GetFields" or "ResolveMethod" or "ResolveMember",
            "System.Delegate" => methodName is "CreateDelegate" or "DynamicInvoke",
            "System.Runtime.CompilerServices.RuntimeHelpers" => methodName is "RunClassConstructor" or "GetUninitializedObject",
            _ => false
        };
    }

    private static (OpCode Op, int OperandStart, int OperandBytes, int Token) DecodeInstruction(
        ReadOnlySpan<byte> il, int ip, string ownerLabel)
    {
        int instrOffset = ip;
        bool isTwoByte = il[ip] == 0xFE;
        OpCode op = isTwoByte
            ? (++ip < il.Length ? CilOpcodeTable.TwoByte[il[ip++]] : throw new InvalidOperationException($"Truncated 0xFE opcode in {ownerLabel}"))
            : CilOpcodeTable.OneByte[il[ip++]];
        if (op.Name is null) { throw new InvalidOperationException($"Unknown CIL opcode at IL_{instrOffset:X4} in {ownerLabel}"); }

        int operandStart = ip;
        int operandBytes;
        int token = 0;
        if (op.OperandType == OperandType.InlineNone) { operandBytes = 0; }
        else if (op.OperandType is OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar) { operandBytes = 1; }
        else if (op.OperandType == OperandType.InlineVar) { operandBytes = 2; }
        else if (op.OperandType is OperandType.InlineI or OperandType.InlineBrTarget or OperandType.ShortInlineR) { operandBytes = 4; }
        else if (op.OperandType is OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineSig
            or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType)
        {
            operandBytes = 4;
            token = BinaryPrimitives.ReadInt32LittleEndian(il.Slice(ip, 4));
        }
        else if (op.OperandType is OperandType.InlineI8 or OperandType.InlineR) { operandBytes = 8; }
        else if (op.OperandType == OperandType.InlineSwitch)
        {
            int count = BinaryPrimitives.ReadInt32LittleEndian(il.Slice(ip, 4));
            operandBytes = checked(4 + (count * 4));
        }
        else { throw new NotSupportedException($"Unsupported operand type {op.OperandType} at IL_{instrOffset:X4} in {ownerLabel}"); }

        return (op, operandStart, operandBytes, token);
    }

    private static string GetTypeDefFullName(MetadataReader mr, TypeDefinitionHandle handle)
    {
        TypeDefinition td = mr.GetTypeDefinition(handle);
        string name = mr.GetString(td.Name);
        if (td.IsNested) { return GetTypeDefFullName(mr, td.GetDeclaringType()) + "+" + name; }
        string ns = mr.GetString(td.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    private static string GetTypeRefFullName(MetadataReader mr, TypeReferenceHandle handle)
    {
        TypeReference tr = mr.GetTypeReference(handle);
        string name = mr.GetString(tr.Name);
        if (tr.ResolutionScope.Kind == HandleKind.TypeReference) { return GetTypeRefFullName(mr, (TypeReferenceHandle)tr.ResolutionScope) + "+" + name; }
        string ns = mr.GetString(tr.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    private static string ResolveTypeHandle(MetadataReader mr, EntityHandle handle, SignatureTypeProvider provider, GenericContext gctx)
    {
        if (handle.Kind == HandleKind.TypeDefinition) { return provider.GetTypeFromDefinition(mr, (TypeDefinitionHandle)handle, 0); }
        if (handle.Kind == HandleKind.TypeReference) { return provider.GetTypeFromReference(mr, (TypeReferenceHandle)handle, 0); }
        return handle.Kind == HandleKind.TypeSpecification ? provider.GetTypeFromSpecification(mr, gctx, (TypeSpecificationHandle)handle, 0) : string.Empty;
    }

    private static bool TryResolveMethodTarget(
        MetadataReader mr, EntityHandle handle, SignatureTypeProvider provider, GenericContext gctx,
        out string declaringType, out string methodName)
    {
        if (handle.Kind == HandleKind.MethodDefinition)
        {
            MethodDefinition md = mr.GetMethodDefinition((MethodDefinitionHandle)handle);
            declaringType = provider.GetTypeFromDefinition(mr, md.GetDeclaringType(), 0);
            methodName = mr.GetString(md.Name);
            return true;
        }
        if (handle.Kind == HandleKind.MemberReference)
        {
            MemberReference mref = mr.GetMemberReference((MemberReferenceHandle)handle);
            declaringType = ResolveTypeHandle(mr, mref.Parent, provider, gctx);
            methodName = mr.GetString(mref.Name);
            return true;
        }
        if (handle.Kind == HandleKind.MethodSpecification)
        {
            MethodSpecification mspec = mr.GetMethodSpecification((MethodSpecificationHandle)handle);
            return TryResolveMethodTarget(mr, mspec.Method, provider, gctx, out declaringType, out methodName);
        }
        declaringType = string.Empty;
        methodName = string.Empty;
        return false;
    }

    private readonly record struct GenericContext(string AssemblyName, string TypeName, string MethodName);

    private sealed class SignatureTypeProvider : ISignatureTypeProvider<string, GenericContext>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => GetTypeDefFullName(reader, handle);
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => GetTypeRefFullName(reader, handle);
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[{new string(',', Math.Max(0, shape.Rank - 1))}]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
            $"{genericType}<{string.Join(", ", typeArguments)}>";
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetFunctionPointerType(MethodSignature<string> signature) => "fn*";
        public string GetGenericMethodParameter(GenericContext genericContext, int index) => $"!!{index}";
        public string GetGenericTypeParameter(GenericContext genericContext, int index) => $"!{index}";
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetPinnedType(string elementType) => elementType;
        public string GetTypeFromSpecification(MetadataReader reader, GenericContext genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    }

    private static class CilOpcodeTable
    {
        internal static readonly OpCode[] OneByte = new OpCode[256];
        internal static readonly OpCode[] TwoByte = new OpCode[256];

        static CilOpcodeTable()
        {
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetValue(null) is OpCode op)
                {
                    ushort raw = unchecked((ushort)op.Value);
                    if (op.Size == 1) { OneByte[raw] = op; }
                    else if (op.Size == 2 && (raw >> 8) == 0xFE) { TwoByte[raw & 0xFF] = op; }
                }
            }
        }
    }
}
