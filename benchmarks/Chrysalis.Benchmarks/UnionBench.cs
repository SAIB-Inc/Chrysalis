using System.Buffers;
using System.Linq.Expressions;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core.Certificates;
using Chrysalis.Codec.Types.Cardano.Core.Governance;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Benchmarks;

[MemoryDiagnoser]
public class UnionBench
{
    private Func<int> _read = null!, _switch = null!, _construct = null!, _nested = null!;

    internal readonly record struct NestedSpec(
        Type ParentType,
        string PropertyName,
        Type? ListItemUnionType,
        ReadOnlyMemory<byte>[] Inputs);

    [GlobalSetup]
    public void Setup()
    {
        byte[] hash28 = [.. Enumerable.Range(1, 28).Select(static i => (byte)i)];
        byte[] hash29 = [0xE1, .. hash28];
        byte[] hash32 = [.. Enumerable.Range(1, 32).Select(static i => (byte)i)];

        ReadOnlyMemory<byte> nonce0 = Encode(static w => { w.WriteBeginArray(1); w.WriteInt32(0); });
        ReadOnlyMemory<byte> nonce1 = Encode(w => { w.WriteBeginArray(2); w.WriteInt32(1); w.WriteByteString(hash32); });

        static void WritePot(CborWriter w) => w.WriteUInt64(5_000_000);
        void WriteCreds(CborWriter w)
        {
            w.WriteBeginMap(16);
            byte[] credHash = [.. hash28];
            for (int i = 0; i < 16; i++)
            {
                credHash[0] = (byte)(i + 1);
                w.WriteBeginArray(2);
                w.WriteInt32(0);
                w.WriteByteString(credHash);
                w.WriteInt64(1_000L + i);
            }
        }
        ReadOnlyMemory<byte> targetPot = Encode(WritePot);
        ReadOnlyMemory<byte> targetCreds = Encode(WriteCreds);

        static void WriteRelayAddr(CborWriter w) { w.WriteBeginArray(4); w.WriteInt32(0); w.WriteInt32(3001); w.WriteByteString([127, 0, 0, 1]); w.WriteNull(); }
        static void WriteRelayHost(CborWriter w) { w.WriteBeginArray(3); w.WriteInt32(1); w.WriteInt32(3001); w.WriteString("relay.example.com"); }
        static void WriteRelayMulti(CborWriter w) { w.WriteBeginArray(2); w.WriteInt32(2); w.WriteString("pool.example.com"); }
        ReadOnlyMemory<byte> relayAddr = Encode(WriteRelayAddr);
        ReadOnlyMemory<byte> relayHost = Encode(WriteRelayHost);
        ReadOnlyMemory<byte> relayMulti = Encode(WriteRelayMulti);

        void WriteDRepKey(CborWriter w) { w.WriteBeginArray(2); w.WriteInt32(0); w.WriteByteString(hash28); }
        void WriteDRepScript(CborWriter w) { w.WriteBeginArray(2); w.WriteInt32(1); w.WriteByteString(hash28); }
        static void WriteDRepAbstain(CborWriter w) { w.WriteBeginArray(1); w.WriteInt32(2); }
        static void WriteDRepNoConf(CborWriter w) { w.WriteBeginArray(1); w.WriteInt32(3); }
        ReadOnlyMemory<byte> drepKey = Encode(WriteDRepKey);
        ReadOnlyMemory<byte> drepScript = Encode(WriteDRepScript);
        ReadOnlyMemory<byte> drepAbstain = Encode(WriteDRepAbstain);
        ReadOnlyMemory<byte> drepNoConf = Encode(WriteDRepNoConf);

        static void WritePpBabbage(CborWriter w) { w.WriteBeginMap(1); w.WriteInt32(14); w.WriteUInt64(8); }
        static void WritePpConway(CborWriter w) { w.WriteBeginMap(1); w.WriteInt32(27); w.WriteUInt64(1); }
        ReadOnlyMemory<byte> ppBabbage = Encode(WritePpBabbage);
        ReadOnlyMemory<byte> ppConway = Encode(WritePpConway);

        ReadOnlyMemory<byte> mirPot = Encode(static w => { w.WriteBeginArray(2); w.WriteInt32(0); WritePot(w); });
        ReadOnlyMemory<byte> mirCreds = Encode(w => { w.WriteBeginArray(2); w.WriteInt32(1); WriteCreds(w); });
        ReadOnlyMemory<byte> poolReg = Encode(w =>
        {
            w.WriteBeginArray(10); w.WriteInt32(3); w.WriteByteString(hash28); w.WriteByteString(hash32);
            w.WriteUInt64(500_000_000); w.WriteUInt64(340_000_000);
            w.WriteSemanticTag(30); w.WriteBeginArray(2); w.WriteUInt64(1); w.WriteUInt64(100);
            w.WriteByteString(hash29); w.WriteBeginArray(0); w.WriteBeginArray(3);
            WriteRelayAddr(w); WriteRelayHost(w); WriteRelayMulti(w); w.WriteNull();
        });
        ReadOnlyMemory<byte>[] voteCerts =
        [
            EncodeVote(WriteDRepKey, hash28), EncodeVote(WriteDRepScript, hash28),
            EncodeVote(WriteDRepAbstain, hash28), EncodeVote(WriteDRepNoConf, hash28)
        ];
        ReadOnlyMemory<byte>[] paramChanges = [EncodeParamChange(WritePpBabbage), EncodeParamChange(WritePpConway)];

        Type nonceType = typeof(NonceWithHash).Assembly.GetType(typeof(NonceWithHash).Namespace + ".Nonce")
            ?? typeof(NonceWithHash).Assembly.GetType(typeof(NonceWithHash).Namespace + ".INonce")!;
        Type targetType = typeof(MoveInstantaneousReward).GetProperty(nameof(MoveInstantaneousReward.Target))!.PropertyType;
        PropertyInfo relaysProp = typeof(PoolRegistration).GetProperty(nameof(PoolRegistration.Relays))!;
        Type relayType = relaysProp.PropertyType.GetGenericArguments()[0];
        Type drepType = typeof(VoteDelegCert).GetProperty(nameof(VoteDelegCert.DRep))!.PropertyType;
        Type ppType = typeof(ParameterChangeAction).GetProperty(nameof(ParameterChangeAction.ProtocolParamUpdate))!.PropertyType;

        (Type UnionType, ReadOnlyMemory<byte>[] Inputs)[] directGroups =
        [
            (nonceType, [nonce0, nonce1]),
            (targetType, [targetPot, targetCreds]),
            (relayType, [relayAddr, relayHost, relayMulti]),
            (drepType, [drepKey, drepScript, drepAbstain, drepNoConf]),
            (ppType, [ppBabbage, ppConway])
        ];

        _read = BuildReadAll(directGroups);
        _switch = BuildSwitchAll(directGroups);
        _construct = BuildConstructAll(
        [
            (nonceType, [(typeof(NonceWithoutHash), nonce0), (typeof(NonceWithHash), nonce1)]),
            (targetType, [(typeof(OtherAccountingPot), targetPot), (typeof(StakeCredentials), targetCreds)]),
            (relayType, [(typeof(SingleHostAddr), relayAddr), (typeof(SingleHostName), relayHost), (typeof(MultiHostName), relayMulti)]),
            (drepType, [(typeof(DRepAddrKeyHash), drepKey), (typeof(DRepScriptHash), drepScript), (typeof(Abstain), drepAbstain), (typeof(DRepNoConfidence), drepNoConf)]),
            (ppType, [(typeof(BabbageProtocolParamUpdate), ppBabbage), (typeof(ConwayProtocolParamUpdate), ppConway)])
        ]);
        _nested = BuildNestedAll(
        [
            new(typeof(MoveInstantaneousReward), nameof(MoveInstantaneousReward.Target), null, [mirPot, mirCreds]),
            new(typeof(PoolRegistration), nameof(PoolRegistration.Relays), relayType, [poolReg]),
            new(typeof(VoteDelegCert), nameof(VoteDelegCert.DRep), null, voteCerts),
            new(typeof(ParameterChangeAction), nameof(ParameterChangeAction.ProtocolParamUpdate), null, paramChanges)
        ]);
    }

    [Benchmark] public int ReadFirstFamily() => _read();
    [Benchmark] public int SwitchFirstFamily() => _switch();
    [Benchmark] public int ConstructFirstFamily() => _construct();
    [Benchmark] public int NestedFirstFamily() => _nested();

    private static byte[] Encode(Action<CborWriter> write)
    {
        ArrayBufferWriter<byte> buffer = new();
        write(new CborWriter(buffer));
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] EncodeVote(Action<CborWriter> writeDRep, byte[] hash28) => Encode(w =>
    {
        w.WriteBeginArray(3); w.WriteInt32(9); w.WriteBeginArray(2); w.WriteInt32(0); w.WriteByteString(hash28); writeDRep(w);
    });

    private static byte[] EncodeParamChange(Action<CborWriter> writePp) => Encode(w =>
    {
        w.WriteBeginArray(4); w.WriteInt32(0); w.WriteNull(); writePp(w); w.WriteNull();
    });

    private static PropertyInfo ResolveRawProp(Type type) =>
        type.GetProperty("Raw") ?? typeof(ICborType).GetProperty(nameof(ICborType.Raw))!;

    private static MethodInfo ResolveReadMethod(Type type) =>
        type.GetMethod("Read", BindingFlags.Public | BindingFlags.Static, [typeof(ReadOnlyMemory<byte>), typeof(int).MakeByRefType()])!;

    private static Type[] DiscoverCaseTypes(Type unionType) =>
        ModelGraph.IsOfficialUnion(unionType)
            ? [.. ModelGraph.OfficialUnionTryGetters(unionType).Select(static m => m.GetParameters()[0].ParameterType.GetElementType()!)]
            : [.. unionType.Assembly.GetTypes()
                .Where(t => !t.IsInterface && !t.IsAbstract && !t.IsGenericTypeDefinition && unionType.IsAssignableFrom(t))
                .OrderBy(static t => t.FullName, StringComparer.Ordinal)];

    private static BlockExpression BuildSwitchExpr(Type unionType, Expression unionExpr)
    {
        ParameterExpression unionLocal = Expression.Variable(unionType, "u");
        if (ModelGraph.IsOfficialUnion(unionType))
        {
            MethodInfo[] getters = ModelGraph.OfficialUnionTryGetters(unionType);
            List<ParameterExpression> locals = [unionLocal];
            Expression chain = Expression.Constant(0);
            for (int i = getters.Length - 1; i >= 0; i--)
            {
                MethodInfo getter = getters[i];
                Type caseType = getter.GetParameters()[0].ParameterType.GetElementType()!;
                ParameterExpression caseLocal = Expression.Variable(caseType, "c" + i);
                locals.Add(caseLocal);
                Expression len = Expression.Property(Expression.Property(caseLocal, ResolveRawProp(caseType)), nameof(ReadOnlyMemory<>.Length));
                chain = Expression.Condition(Expression.Call(unionLocal, getter, caseLocal), len, chain);
            }
            return Expression.Block(locals, Expression.Assign(unionLocal, unionExpr),
                Expression.Condition(Expression.Property(unionLocal, unionType.GetProperty("HasValue")!), chain, Expression.Constant(0)));
        }

        Type[] cases = DiscoverCaseTypes(unionType);
        Expression legacyChain = Expression.Constant(0);
        for (int i = cases.Length - 1; i >= 0; i--)
        {
            Type caseType = cases[i];
            Expression len = Expression.Property(Expression.Property(Expression.Convert(unionLocal, caseType), ResolveRawProp(caseType)), nameof(ReadOnlyMemory<>.Length));
            legacyChain = Expression.Condition(Expression.TypeIs(unionLocal, caseType), len, legacyChain);
        }
        return Expression.Block([unionLocal], Expression.Assign(unionLocal, unionExpr), legacyChain);
    }

    private static int SwitchRawList<TUnion>(ReadOnlyMemory<byte> raw, Func<TUnion, int> switchOne)
    {
        int sum = 0;
        CborListEnumerator<TUnion> enumerator = new(raw);
        while (enumerator.MoveNext()) { sum = unchecked(sum + switchOne(enumerator.Current)); }
        return sum;
    }

    internal static Func<int> BuildReadAll((Type UnionType, ReadOnlyMemory<byte>[] Inputs)[] groups)
    {
        ParameterExpression sum = Expression.Variable(typeof(int), "sum");
        ParameterExpression consumed = Expression.Variable(typeof(int), "consumed");
        List<ParameterExpression> locals = [sum, consumed];
        List<Expression> body = [Expression.Assign(sum, Expression.Constant(0))];
        for (int g = 0; g < groups.Length; g++)
        {
            (Type unionType, ReadOnlyMemory<byte>[] inputs) = groups[g];
            MethodInfo readMethod = ResolveReadMethod(unionType);
            PropertyInfo rawProp = ResolveRawProp(unionType);
            ParameterExpression unionVar = Expression.Variable(unionType, "u" + g);
            locals.Add(unionVar);
            foreach (ReadOnlyMemory<byte> input in inputs)
            {
                body.Add(Expression.Assign(unionVar, Expression.Call(readMethod, Expression.Constant(input), consumed)));
                body.Add(Expression.AddAssign(sum, Expression.Property(Expression.Property(unionVar, rawProp), nameof(ReadOnlyMemory<>.Length))));
                body.Add(Expression.AddAssign(sum, consumed));
            }
        }
        body.Add(sum);
        return Expression.Lambda<Func<int>>(Expression.Block(locals, body)).Compile();
    }

    internal static Func<int> BuildSwitchAll((Type UnionType, ReadOnlyMemory<byte>[] Inputs)[] groups)
    {
        ParameterExpression sum = Expression.Variable(typeof(int), "sum");
        List<Expression> body = [Expression.Assign(sum, Expression.Constant(0))];
        foreach ((Type unionType, ReadOnlyMemory<byte>[] inputs) in groups)
        {
            MethodInfo readMethod = ResolveReadMethod(unionType);
            Array values = Array.CreateInstance(unionType, inputs.Length);
            for (int i = 0; i < inputs.Length; i++) { values.SetValue(readMethod.Invoke(null, [inputs[i], 0])!, i); }
            ConstantExpression arrExpr = Expression.Constant(values, unionType.MakeArrayType());
            for (int i = 0; i < inputs.Length; i++)
            {
                body.Add(Expression.AddAssign(sum, BuildSwitchExpr(unionType, Expression.ArrayIndex(arrExpr, Expression.Constant(i)))));
            }
        }
        body.Add(sum);
        return Expression.Lambda<Func<int>>(Expression.Block([sum], body)).Compile();
    }

    internal static Func<int> BuildConstructAll((Type UnionType, (Type CaseType, ReadOnlyMemory<byte> Wire)[] Cases)[] groups)
    {
        ParameterExpression sum = Expression.Variable(typeof(int), "sum");
        List<ParameterExpression> locals = [sum];
        List<Expression> body = [Expression.Assign(sum, Expression.Constant(0))];
        for (int g = 0; g < groups.Length; g++)
        {
            (Type unionType, (Type CaseType, ReadOnlyMemory<byte> Wire)[] cases) = groups[g];
            bool official = ModelGraph.IsOfficialUnion(unionType);
            PropertyInfo rawProp = ResolveRawProp(unionType);
            ParameterExpression unionVar = Expression.Variable(unionType, "u" + g);
            locals.Add(unionVar);
            foreach ((Type caseType, ReadOnlyMemory<byte> wire) in cases)
            {
                Array caseArr = Array.CreateInstance(caseType, 1);
                caseArr.SetValue(ResolveReadMethod(caseType).Invoke(null, [wire, 0])!, 0);
                Expression caseExpr = Expression.ArrayIndex(Expression.Constant(caseArr, caseType.MakeArrayType()), Expression.Constant(0));
                Expression constructed = official ? Expression.New(unionType.GetConstructor([caseType])!, caseExpr) : Expression.Convert(caseExpr, unionType);
                body.Add(Expression.Assign(unionVar, constructed));
                body.Add(Expression.AddAssign(sum, Expression.Property(Expression.Property(unionVar, rawProp), nameof(ReadOnlyMemory<>.Length))));
            }
        }
        body.Add(sum);
        return Expression.Lambda<Func<int>>(Expression.Block(locals, body)).Compile();
    }

    internal static Func<int> BuildNestedAll(NestedSpec[] specs)
    {
        ParameterExpression sum = Expression.Variable(typeof(int), "sum");
        ParameterExpression consumed = Expression.Variable(typeof(int), "consumed");
        List<ParameterExpression> locals = [sum, consumed];
        List<Expression> body = [Expression.Assign(sum, Expression.Constant(0))];
        for (int s = 0; s < specs.Length; s++)
        {
            NestedSpec spec = specs[s];
            MethodInfo readParent = ResolveReadMethod(spec.ParentType);
            PropertyInfo prop = spec.ParentType.GetProperty(spec.PropertyName)!;
            _ = prop.GetValue(readParent.Invoke(null, [spec.Inputs[0], 0])!);
            ParameterExpression parentVar = Expression.Variable(spec.ParentType, "p" + s);
            locals.Add(parentVar);
            if (spec.ListItemUnionType is Type itemUnionType)
            {
                ParameterExpression listVar = Expression.Variable(prop.PropertyType, "l" + s);
                locals.Add(listVar);
                ParameterExpression itemParam = Expression.Parameter(itemUnionType, "item");
                Type funcType = typeof(Func<,>).MakeGenericType(itemUnionType, typeof(int));
                Delegate switchOne = Expression.Lambda(funcType, BuildSwitchExpr(itemUnionType, itemParam), itemParam).Compile();
                PropertyInfo listRawProp = ResolveRawProp(prop.PropertyType);
                MethodInfo switchList = typeof(UnionBench).GetMethod(nameof(SwitchRawList), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(itemUnionType);
                foreach (ReadOnlyMemory<byte> input in spec.Inputs)
                {
                    body.Add(Expression.Assign(parentVar, Expression.Call(readParent, Expression.Constant(input), consumed)));
                    body.Add(Expression.Assign(listVar, Expression.Property(parentVar, prop)));
                    body.Add(Expression.AddAssign(sum, Expression.Call(switchList, Expression.Property(listVar, listRawProp), Expression.Constant(switchOne, funcType))));
                }
            }
            else
            {
                foreach (ReadOnlyMemory<byte> input in spec.Inputs)
                {
                    body.Add(Expression.Assign(parentVar, Expression.Call(readParent, Expression.Constant(input), consumed)));
                    body.Add(Expression.AddAssign(sum, BuildSwitchExpr(prop.PropertyType, Expression.Property(parentVar, prop))));
                }
            }
        }
        body.Add(sum);
        return Expression.Lambda<Func<int>>(Expression.Block(locals, body)).Compile();
    }
}
