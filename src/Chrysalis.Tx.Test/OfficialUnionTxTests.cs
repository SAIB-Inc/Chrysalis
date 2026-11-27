using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Certificates;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Governance;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Plutus.Types;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Xunit;

namespace Chrysalis.Tx.Test;

public sealed class OfficialUnionTxTests
{
    private static readonly byte[] AddressBytes = [0x60, .. new byte[28]];
    private static readonly ResolvedInput Funds = new(
        TransactionInput.Create(new byte[32], 0),
        PostAlonzoTransactionOutput.Create(new Address(AddressBytes), Lovelace.Create(5_000_000)));

    [Fact]
    public async Task TxBuilderAddVoteDelegationAndScriptContextDRepToPlutusDataCoverAllCases()
    {
        byte[] keyHash = [.. Enumerable.Repeat((byte)0x11, 28)];
        byte[] scriptHash = [.. Enumerable.Repeat((byte)0x22, 28)];
        Credential delegator = Credential.Create(0, keyHash);

        (DRep Union, string ExpectedLabel, long[] ExpectedTags)[] cases =
        [
            (DRepAddrKeyHash.Create(0, keyHash), "addr", [0, 0]),
            (DRepScriptHash.Create(1, scriptHash), "script", [0, 1]),
            (Abstain.Create(2), "abstain", [1]),
            (DRepNoConfidence.Create(3), "noconf", [2])
        ];

        ProtocolParams pp = new(
            44, 155381, null, 16384, null, null, null, null, null, null, null, null, null, null,
            4310, null, ExUnitPrices.Create(new CborRationalNumber(577, 10000), new CborRationalNumber(721, 10000000)),
            null, null, null, 150, 3, null, null, null, null, null, null, null, null, new CborRationalNumber(15, 1));
        string changeAddr = CliSnapshotProvider.Bech32(Funds.Output);

        foreach ((DRep drep, string label, long[] expectedTags) in cases)
        {
            PostMaryTransaction built = await new TxBuilder(new CliSnapshotProvider(pp, [Funds]))
                .AddInput(Funds)
                .AddVoteDelegation(delegator, drep)
                .PayLovelace(changeAddr, 2_000_000)
                .SetChangeAddress(changeAddr)
                .Complete();
            ConwayTransactionBody body = Assert.IsType<ConwayTransactionBody>(built.Body);
            VoteDelegCert cert = Assert.IsType<VoteDelegCert>(Assert.Single(body.Certificates!.Value));
            Assert.Equal(drep.Raw.ToArray(), cert.DRep.Raw.ToArray());
            Assert.True(cert.DRep.Equals(cert.DRep));

            PlutusDataConstr txInfo = Assert.IsType<PlutusDataConstr>(
                ScriptContextBuilder.BuildTxInfoV3(body, (PostAlonzoTransactionWitnessSet)built.Witnesses, [Funds], SlotNetworkConfig.Preprod));
            PlutusDataList certs = Assert.IsType<PlutusDataList>(txInfo.Fields[5]);
            PlutusDataConstr txCert = Assert.IsType<PlutusDataConstr>(Assert.Single(certs.Values));
            Assert.Equal(2, (long)txCert.Tag);
            PlutusDataConstr delegatee = Assert.IsType<PlutusDataConstr>(txCert.Fields[1]);
            Assert.Equal(1, (long)delegatee.Tag);
            PlutusDataConstr drepData = Assert.IsType<PlutusDataConstr>(Assert.Single(delegatee.Fields));
            Assert.Equal(expectedTags[0], (long)drepData.Tag);
            if (expectedTags.Length == 2)
            {
                PlutusDataConstr inner = Assert.IsType<PlutusDataConstr>(Assert.Single(drepData.Fields));
                Assert.Equal(expectedTags[1], (long)inner.Tag);
            }
            else
            {
                Assert.Empty(drepData.Fields);
            }
            _ = label;
        }

        // Verify implicit conversion overload directly on TxBuilder.AddVoteDelegation and default(DRep) rejection in DRepToPlutusData.
        TxBuilder direct = new TxBuilder(new CliSnapshotProvider(pp, [Funds]))
            .AddVoteDelegation(delegator, Abstain.Create(2));
        Assert.NotNull(direct);

        MethodInfo drepToPlutus = typeof(ScriptContextBuilder).GetMethod("DRepToPlutusData", BindingFlags.Static | BindingFlags.NonPublic)!;
        TargetInvocationException tie = Assert.Throws<TargetInvocationException>(() => drepToPlutus.Invoke(null, [default(DRep)]));
        _ = Assert.IsType<InvalidOperationException>(tie.InnerException);
    }

    [Fact]
    public void SolutionHasZeroUnionBoxingAndZeroValueAccessAcrossAll14Assemblies()
    {
        string repoRoot = FindRepoRoot();
        string config = ResolveBuildConfiguration();
        List<string> dllPaths = DiscoverShippedAssemblies(repoRoot, config);
        Assert.Equal(14, dllPaths.Count);

        foreach (string dllPath in dllPaths)
        {
            Assert.True(File.Exists(dllPath), $"Expected compiled assembly missing: {dllPath}");
        }

        HashSet<(string AssemblyName, string FullName)> unionTypes = CollectUnionTypes(dllPaths);
        Assert.Contains(("Chrysalis.Codec", "Chrysalis.Codec.Types.Cardano.Core.Governance.DRep"), unionTypes);
        Assert.Contains(("Chrysalis.Codec", "Chrysalis.Codec.Types.Cardano.Core.Protocol.Nonce"), unionTypes);
        Assert.Contains(("Chrysalis.Codec", "Chrysalis.Codec.Types.Cardano.Core.Certificates.Relay"), unionTypes);
        Assert.Contains(("Chrysalis.Codec", "Chrysalis.Codec.Types.Cardano.Core.Certificates.Target"), unionTypes);
        Assert.Contains(("Chrysalis.Codec", "Chrysalis.Codec.Types.Cardano.Core.Protocol.ProtocolParamUpdate"), unionTypes);

        List<string> violations = [];
        foreach (string dllPath in dllPaths)
        {
            using FileStream fs = File.OpenRead(dllPath);
            using PEReader pe = new(fs);
            violations.AddRange(ScanAssembly(pe, unionTypes, Path.GetFileName(dllPath)));
        }

        Assert.True(violations.Count == 0,
            "Found forbidden [Union].Value or union boxing in shipped IL:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void UnionIlGuardDetectsAllFiveViolationShapes()
    {
        string codecDll = typeof(DRep).Assembly.Location;
        HashSet<(string AssemblyName, string FullName)> unionTypes = CollectUnionTypes([codecDll]);
        MethodInfo unionGetValue = typeof(DRep).GetProperty(nameof(DRep.Value))!.GetGetMethod()!;
        MethodInfo iUnionGetValue = typeof(IUnion).GetProperty(nameof(IUnion.Value))!.GetGetMethod()!;

        // 1. Direct DRep.get_Value call + ldftn.
        byte[] pe1 = EmitTestAssembly("ProbeDirectValue", il =>
        {
            il.DeclareLocal(typeof(DRep));
            il.Emit(OpCodes.Ldloca_S, (byte)0);
            il.Emit(OpCodes.Call, unionGetValue);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldftn, unionGetValue);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
        });
        List<string> hits1 = ScanPeBytes(pe1, unionTypes, "ProbeDirectValue.dll");
        Assert.Equal(2, hits1.Count);

        // 2. constrained. DRep / generic constrained. + callvirt IUnion::get_Value + ldvirtftn.
        byte[] pe2 = EmitGenericIUnionAssembly("ProbeConstrainedIUnion", iUnionGetValue);
        List<string> hits2 = ScanPeBytes(pe2, unionTypes, "ProbeConstrainedIUnion.dll");
        Assert.Equal(3, hits2.Count);
        Assert.Contains(hits2, static h => h.Contains("ldvirtftn", StringComparison.Ordinal));

        // 3. box DRep alone.
        byte[] pe3 = EmitTestAssembly("ProbeBoxUnion", il =>
        {
            il.DeclareLocal(typeof(DRep));
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Box, typeof(DRep));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
        });
        List<string> hits3 = ScanPeBytes(pe3, unionTypes, "ProbeBoxUnion.dll");
        _ = Assert.Single(hits3);
        Assert.Contains("box", hits3[0], StringComparison.Ordinal);

        // 4. box Nullable<DRep> alone and even directly before newobj SwitchExpressionException + throw.
        ConstructorInfo switchExCtor = typeof(SwitchExpressionException).GetConstructor([typeof(object)])!;
        byte[] pe4 = EmitTestAssembly("ProbeBoxNullableUnion", il =>
        {
            il.DeclareLocal(typeof(DRep?));
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Box, typeof(DRep?));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Box, typeof(DRep?));
            il.Emit(OpCodes.Newobj, switchExCtor);
            il.Emit(OpCodes.Throw);
        });
        List<string> hits4 = ScanPeBytes(pe4, unionTypes, "ProbeBoxNullableUnion.dll");
        Assert.Equal(2, hits4.Count);
        Assert.All(hits4, static h => Assert.Contains("box", h, StringComparison.Ordinal));

        // 5. External provider-style compiled assembly violating both .Value and box Nullable<DRep>,
        // plus box DRep followed by newobj SwitchExpressionException without immediate throw.
        byte[] pe5 = EmitTestAssembly("Chrysalis.Tx.Providers.Synthetic", il =>
        {
            il.DeclareLocal(typeof(DRep));
            il.DeclareLocal(typeof(DRep?));
            il.Emit(OpCodes.Ldloca_S, (byte)0);
            il.Emit(OpCodes.Call, unionGetValue);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldloc_1);
            il.Emit(OpCodes.Box, typeof(DRep?));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Box, typeof(DRep));
            il.Emit(OpCodes.Newobj, switchExCtor);
            il.Emit(OpCodes.Nop);
            il.Emit(OpCodes.Throw);
        });
        List<string> hits5 = ScanPeBytes(pe5, unionTypes, "Chrysalis.Tx.Providers.Synthetic.dll");
        Assert.Equal(3, hits5.Count);

        // 6. Positive and negative self-tests on real Roslyn-compiled methods in this test assembly.
        Assert.Equal(-1, PositiveSwitchProbe.TagSwitch(default));
        Assert.Null(PositiveSwitchProbe.HashSwitch(default));
        Assert.Equal(typeof(DRep), NegativeViolationProbe.BoxViaGetType(Abstain.Create(2)));
        Assert.Equal(-1, NegativeViolationProbe.BoxViaInterface(true, Abstain.Create(2), Abstain.Create(2)));
        Assert.IsType<Abstain>(NegativeViolationProbe.DirectValue(Abstain.Create(2)));

        string selfDll = typeof(OfficialUnionTxTests).Assembly.Location;
        using FileStream selfFs = File.OpenRead(selfDll);
        using PEReader selfPe = new(selfFs);
        List<string> positiveHits = ScanAssembly(selfPe, unionTypes, "SelfPositive.dll",
            static typeName => typeName.EndsWith("+PositiveSwitchProbe", StringComparison.Ordinal));
        Assert.Empty(positiveHits);

        List<string> negativeHits = ScanAssembly(selfPe, unionTypes, "SelfNegative.dll",
            static typeName => typeName.EndsWith("+NegativeViolationProbe", StringComparison.Ordinal));
        Assert.Equal(3, negativeHits.Count);
        Assert.Contains(negativeHits, static h => h.Contains("BoxViaGetType", StringComparison.Ordinal) && h.Contains("box", StringComparison.Ordinal));
        Assert.Contains(negativeHits, static h => h.Contains("BoxViaInterface", StringComparison.Ordinal) && h.Contains("box", StringComparison.Ordinal));
        Assert.Contains(negativeHits, static h => h.Contains("DirectValue", StringComparison.Ordinal) && h.Contains("get_Value", StringComparison.Ordinal));
    }

    private static class PositiveSwitchProbe
    {
        internal static int TagSwitch(DRep drep) => drep switch
        {
            DRepAddrKeyHash k => k.Tag,
            DRepScriptHash s => s.Tag,
            Abstain a => a.Tag,
            DRepNoConfidence n => n.Tag,
            null => -1
        };

        internal static ReadOnlyMemory<byte>? HashSwitch(DRep drep) => drep switch
        {
            DRepAddrKeyHash k => (ReadOnlyMemory<byte>?)k.KeyHash,
            DRepScriptHash s => (ReadOnlyMemory<byte>?)s.ScriptHash,
            Abstain or DRepNoConfidence or null => null
        };
    }

    private static class NegativeViolationProbe
    {
        internal static Type BoxViaGetType(DRep drep) => drep.GetType();
        internal static int BoxViaInterface(bool pickUnion, DRep drep, Abstain fallback) =>
            (pickUnion ? (ICborType)drep : fallback).ConstrIndex;
        internal static object? DirectValue(DRep drep) => drep.Value;
    }

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

    private static byte[] EmitGenericIUnionAssembly(string assemblyName, MethodInfo iUnionGetValue)
    {
        PersistedAssemblyBuilder ab = new(new AssemblyName(assemblyName), typeof(object).Assembly);
        ModuleBuilder mb = ab.DefineDynamicModule("MainModule");
        TypeBuilder tb = mb.DefineType("Probe.GenericIUnion", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract);

        MethodBuilder m1 = tb.DefineMethod("ConstrainedConcrete", MethodAttributes.Public | MethodAttributes.Static, typeof(object), Type.EmptyTypes);
        ILGenerator il1 = m1.GetILGenerator();
        il1.DeclareLocal(typeof(DRep));
        il1.Emit(OpCodes.Ldloca_S, (byte)0);
        il1.Emit(OpCodes.Constrained, typeof(DRep));
        il1.Emit(OpCodes.Callvirt, iUnionGetValue);
        il1.Emit(OpCodes.Ret);

        MethodBuilder m2 = tb.DefineMethod("ConstrainedGeneric", MethodAttributes.Public | MethodAttributes.Static, typeof(object), null);
        GenericTypeParameterBuilder tParam = m2.DefineGenericParameters("T")[0];
        tParam.SetInterfaceConstraints(typeof(IUnion));
        m2.SetParameters(tParam);
        ILGenerator il2 = m2.GetILGenerator();
        il2.Emit(OpCodes.Ldarga_S, (byte)0);
        il2.Emit(OpCodes.Constrained, tParam);
        il2.Emit(OpCodes.Callvirt, iUnionGetValue);
        il2.Emit(OpCodes.Ret);

        MethodBuilder m3 = tb.DefineMethod("VirtualDelegate", MethodAttributes.Public | MethodAttributes.Static, typeof(nint), [typeof(IUnion)]);
        ILGenerator il3 = m3.GetILGenerator();
        il3.Emit(OpCodes.Ldarg_0);
        il3.Emit(OpCodes.Ldvirtftn, iUnionGetValue);
        il3.Emit(OpCodes.Ret);

        _ = tb.CreateType();
        using MemoryStream ms = new();
        ab.Save(ms);
        return ms.ToArray();
    }

    private static string FindRepoRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Chrysalis.slnx")))
            {
                return dir.FullName;
            }
        }
        throw new DirectoryNotFoundException("Could not locate Chrysalis.slnx from AppContext.BaseDirectory.");
    }

    private static string ResolveBuildConfiguration()
    {
        string baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? parentName = new DirectoryInfo(baseDir).Parent?.Name;
        if (parentName is "Debug" or "Release")
        {
            return parentName;
        }
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }

    private static List<string> DiscoverShippedAssemblies(string repoRoot, string config)
    {
        XDocument slnx = XDocument.Load(Path.Combine(repoRoot, "Chrysalis.slnx"));
        List<string> results = [];
        foreach (XElement projectEl in slnx.Descendants("Project"))
        {
            string? relPath = projectEl.Attribute("Path")?.Value;
            if (string.IsNullOrEmpty(relPath) || !relPath.StartsWith("src/", StringComparison.Ordinal))
            {
                continue;
            }
            string fileStem = Path.GetFileNameWithoutExtension(relPath);
            if (fileStem.EndsWith(".Test", StringComparison.Ordinal)
                || fileStem.EndsWith("Tests", StringComparison.Ordinal)
                || fileStem == "Chrysalis.Codec.CodeGen")
            {
                continue;
            }
            string csprojPath = Path.Combine(repoRoot, relPath);
            XDocument csproj = XDocument.Load(csprojPath);
            string assemblyName = csproj.Descendants("AssemblyName").FirstOrDefault()?.Value ?? fileStem;
            string tfm = csproj.Descendants("TargetFramework").FirstOrDefault()?.Value ?? "net11.0";
            string projDir = Path.GetDirectoryName(csprojPath)!;
            results.Add(Path.Combine(projDir, "bin", config, tfm, assemblyName + ".dll"));
        }
        results.Sort(StringComparer.Ordinal);
        return results;
    }

    private static HashSet<(string AssemblyName, string FullName)> CollectUnionTypes(IEnumerable<string> dllPaths)
    {
        HashSet<(string AssemblyName, string FullName)> unions = [];
        foreach (string dllPath in dllPaths)
        {
            using FileStream fs = File.OpenRead(dllPath);
            using PEReader pe = new(fs);
            MetadataReader mr = pe.GetMetadataReader();
            string asmName = mr.GetString(mr.GetAssemblyDefinition().Name);
            foreach (TypeDefinitionHandle tdHandle in mr.TypeDefinitions)
            {
                TypeDefinition td = mr.GetTypeDefinition(tdHandle);
                foreach (CustomAttributeHandle caHandle in td.GetCustomAttributes())
                {
                    CustomAttribute ca = mr.GetCustomAttribute(caHandle);
                    string? attrTypeName = ResolveAttributeTypeFullName(mr, ca.Constructor);
                    if (attrTypeName == "System.Runtime.CompilerServices.UnionAttribute")
                    {
                        _ = unions.Add((asmName, GetTypeDefFullName(mr, tdHandle)));
                    }
                }
            }
        }
        return unions;
    }

    private static List<string> ScanPeBytes(byte[] peBytes, HashSet<(string AssemblyName, string FullName)> unionTypes, string label)
    {
        using MemoryStream ms = new(peBytes, writable: false);
        using PEReader pe = new(ms);
        return ScanAssembly(pe, unionTypes, label);
    }

    private static List<string> ScanAssembly(
        PEReader pe,
        HashSet<(string AssemblyName, string FullName)> unionTypes,
        string assemblyLabel,
        Func<string, bool>? typeFilter = null)
    {
        MetadataReader mr = pe.GetMetadataReader();
        string thisAsm = mr.GetString(mr.GetAssemblyDefinition().Name);
        SignatureTypeProvider provider = new(thisAsm, unionTypes);
        List<string> violations = [];

        foreach (MethodDefinitionHandle mdHandle in mr.MethodDefinitions)
        {
            MethodDefinition md = mr.GetMethodDefinition(mdHandle);
            if (md.RelativeVirtualAddress == 0)
            {
                continue;
            }
            string declaringName = GetTypeDefFullName(mr, md.GetDeclaringType());
            if (typeFilter is not null && !typeFilter(declaringName))
            {
                continue;
            }
            MethodBodyBlock body = pe.GetMethodBody(md.RelativeVirtualAddress);
            byte[]? ilBytes = body.GetILBytes();
            if (ilBytes is null || ilBytes.Length == 0)
            {
                continue;
            }

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
                    || op.Value == OpCodes.Ldvirtftn.Value)
                {
                    if (TryResolveMethodTarget(mr, MetadataTokens.EntityHandle(token), provider, gctx,
                        out TypeShape targetType, out string targetMethod)
                        && targetMethod == "get_Value"
                        && (targetType.IsUnion || targetType.IsIUnion))
                    {
                        violations.Add($"{ownerLabel} IL_{instrOffset:X4}: {op.Name} {targetType.FullName}::{targetMethod}");
                    }
                }
                else if (op.Value == OpCodes.Box.Value)
                {
                    TypeShape boxedType = ResolveTypeHandle(mr, MetadataTokens.EntityHandle(token), provider, gctx);
                    if (boxedType.IsNullableUnion
                        || (boxedType.IsUnion && !IsSwitchExpressionExceptionTail(mr, il, nextIp, provider, gctx, ownerLabel)))
                    {
                        violations.Add($"{ownerLabel} IL_{instrOffset:X4}: box {boxedType.FullName}");
                    }
                }

                ip = nextIp;
            }
        }
        return violations;
    }

    private static (OpCode Op, int OperandStart, int OperandBytes, int Token) DecodeInstruction(
        ReadOnlySpan<byte> il,
        int ip,
        string ownerLabel)
    {
        int instrOffset = ip;
        bool isTwoByte = il[ip] == 0xFE;
        OpCode op;
        if (isTwoByte)
        {
            ip++;
            if (ip >= il.Length)
            {
                throw new InvalidOperationException($"Truncated 0xFE opcode in {ownerLabel}");
            }
            op = CilOpcodeTable.TwoByte[il[ip++]];
        }
        else
        {
            op = CilOpcodeTable.OneByte[il[ip++]];
        }

        if (op.Name is null)
        {
            throw new InvalidOperationException($"Unknown CIL opcode at IL_{instrOffset:X4} in {ownerLabel}");
        }

        int operandStart = ip;
        int operandBytes;
        int token = 0;
        if (op.OperandType == OperandType.InlineNone)
        {
            operandBytes = 0;
        }
        else if (op.OperandType is OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar)
        {
            operandBytes = 1;
        }
        else if (op.OperandType == OperandType.InlineVar)
        {
            operandBytes = 2;
        }
        else if (op.OperandType is OperandType.InlineI or OperandType.InlineBrTarget or OperandType.ShortInlineR)
        {
            operandBytes = 4;
        }
        else if (op.OperandType is OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineSig
            or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType)
        {
            operandBytes = 4;
            token = BinaryPrimitives.ReadInt32LittleEndian(il.Slice(ip, 4));
        }
        else if (op.OperandType is OperandType.InlineI8 or OperandType.InlineR)
        {
            operandBytes = 8;
        }
        else if (op.OperandType == OperandType.InlineSwitch)
        {
            int count = BinaryPrimitives.ReadInt32LittleEndian(il.Slice(ip, 4));
            operandBytes = checked(4 + (count * 4));
        }
        else
        {
            throw new NotSupportedException($"Unsupported operand type {op.OperandType} at IL_{instrOffset:X4} in {ownerLabel}");
        }

        return (op, operandStart, operandBytes, token);
    }

    private static bool IsSwitchExpressionExceptionTail(
        MetadataReader mr,
        ReadOnlySpan<byte> il,
        int nextIp,
        SignatureTypeProvider provider,
        GenericContext gctx,
        string ownerLabel)
    {
        if (nextIp >= il.Length)
        {
            return false;
        }

        (OpCode nextOp, int nextOperandStart, int nextOperandBytes, int nextToken) = DecodeInstruction(il, nextIp, ownerLabel);
        if (nextOp.Value == OpCodes.Call.Value
            && TryResolveMethodTarget(mr, MetadataTokens.EntityHandle(nextToken), provider, gctx, out TypeShape targetType, out string methodName)
            && targetType.FullName == "<PrivateImplementationDetails>"
            && methodName == "ThrowSwitchExpressionException")
        {
            return true;
        }

        if (nextOp.Value == OpCodes.Newobj.Value
            && TryResolveMethodTarget(mr, MetadataTokens.EntityHandle(nextToken), provider, gctx, out TypeShape ctorType, out string ctorName)
            && ctorType.FullName == "System.Runtime.CompilerServices.SwitchExpressionException"
            && ctorName == ".ctor")
        {
            int afterNewObjIp = nextOperandStart + nextOperandBytes;
            if (afterNewObjIp < il.Length)
            {
                (OpCode throwOp, _, _, _) = DecodeInstruction(il, afterNewObjIp, ownerLabel);
                return throwOp.Value == OpCodes.Throw.Value;
            }
        }

        return false;
    }

    private static string? ResolveAttributeTypeFullName(MetadataReader mr, EntityHandle ctorHandle)
    {
        if (ctorHandle.Kind == HandleKind.MemberReference)
        {
            EntityHandle parent = mr.GetMemberReference((MemberReferenceHandle)ctorHandle).Parent;
            if (parent.Kind == HandleKind.TypeReference)
            {
                return GetTypeRefFullName(mr, (TypeReferenceHandle)parent);
            }
            if (parent.Kind == HandleKind.TypeDefinition)
            {
                return GetTypeDefFullName(mr, (TypeDefinitionHandle)parent);
            }
        }
        else if (ctorHandle.Kind == HandleKind.MethodDefinition)
        {
            TypeDefinitionHandle td = mr.GetMethodDefinition((MethodDefinitionHandle)ctorHandle).GetDeclaringType();
            return GetTypeDefFullName(mr, td);
        }
        return null;
    }

    private static string GetTypeDefFullName(MetadataReader mr, TypeDefinitionHandle handle)
    {
        TypeDefinition td = mr.GetTypeDefinition(handle);
        string name = mr.GetString(td.Name);
        if (td.IsNested)
        {
            return GetTypeDefFullName(mr, td.GetDeclaringType()) + "+" + name;
        }
        string ns = mr.GetString(td.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    private static string GetTypeRefFullName(MetadataReader mr, TypeReferenceHandle handle)
    {
        TypeReference tr = mr.GetTypeReference(handle);
        string name = mr.GetString(tr.Name);
        if (tr.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            return GetTypeRefFullName(mr, (TypeReferenceHandle)tr.ResolutionScope) + "+" + name;
        }
        string ns = mr.GetString(tr.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    private static string GetTypeRefAssemblyName(MetadataReader mr, TypeReferenceHandle handle, string thisAssembly)
    {
        TypeReferenceHandle current = handle;
        while (true)
        {
            EntityHandle scope = mr.GetTypeReference(current).ResolutionScope;
            if (scope.Kind == HandleKind.AssemblyReference)
            {
                return mr.GetString(mr.GetAssemblyReference((AssemblyReferenceHandle)scope).Name);
            }
            if (scope.Kind == HandleKind.TypeReference)
            {
                current = (TypeReferenceHandle)scope;
                continue;
            }
            return thisAssembly;
        }
    }

    private static TypeShape ResolveTypeHandle(
        MetadataReader mr,
        EntityHandle handle,
        SignatureTypeProvider provider,
        GenericContext gctx)
    {
        if (handle.Kind == HandleKind.TypeDefinition) { return provider.GetTypeFromDefinition(mr, (TypeDefinitionHandle)handle, 0); }
        if (handle.Kind == HandleKind.TypeReference) { return provider.GetTypeFromReference(mr, (TypeReferenceHandle)handle, 0); }
        if (handle.Kind == HandleKind.TypeSpecification) { return provider.GetTypeFromSpecification(mr, gctx, (TypeSpecificationHandle)handle, 0); }
        return TypeShape.Unknown;
    }

    private static bool TryResolveMethodTarget(
        MetadataReader mr,
        EntityHandle handle,
        SignatureTypeProvider provider,
        GenericContext gctx,
        out TypeShape declaringType,
        out string methodName)
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
        declaringType = TypeShape.Unknown;
        methodName = string.Empty;
        return false;
    }

    private readonly record struct GenericContext(string AssemblyName, string TypeName, string MethodName);

    private readonly record struct TypeShape(string FullName, bool IsUnion, bool IsNullableUnion, bool IsIUnion)
    {
        public static TypeShape Unknown => new(string.Empty, false, false, false);
    }

    private sealed class SignatureTypeProvider(
        string thisAssembly,
        HashSet<(string AssemblyName, string FullName)> unionTypes)
        : ISignatureTypeProvider<TypeShape, GenericContext>
    {
        public TypeShape GetPrimitiveType(PrimitiveTypeCode typeCode) => new(typeCode.ToString(), false, false, false);

        public TypeShape GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            string fullName = GetTypeDefFullName(reader, handle);
            return new(fullName, unionTypes.Contains((thisAssembly, fullName)), false,
                fullName == "System.Runtime.CompilerServices.IUnion");
        }

        public TypeShape GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            string asmName = GetTypeRefAssemblyName(reader, handle, thisAssembly);
            string fullName = GetTypeRefFullName(reader, handle);
            return new(fullName, unionTypes.Contains((asmName, fullName)), false,
                fullName == "System.Runtime.CompilerServices.IUnion");
        }

        public TypeShape GetSZArrayType(TypeShape elementType) => new(elementType.FullName + "[]", false, false, false);

        public TypeShape GetArrayType(TypeShape elementType, ArrayShape shape) =>
            new($"{elementType.FullName}[{new string(',', Math.Max(0, shape.Rank - 1))}]", false, false, false);

        public TypeShape GetByReferenceType(TypeShape elementType) => new(elementType.FullName + "&", false, false, false);

        public TypeShape GetGenericInstantiation(TypeShape genericType, ImmutableArray<TypeShape> typeArguments)
        {
            string args = string.Join(", ", typeArguments.Select(static a => a.FullName));
            string fullName = $"{genericType.FullName}<{args}>";
            bool isNullableUnion = genericType.FullName == "System.Nullable`1"
                && typeArguments.Length == 1
                && typeArguments[0].IsUnion;
            return new(fullName, genericType.IsUnion, isNullableUnion, false);
        }

        public TypeShape GetPointerType(TypeShape elementType) => new(elementType.FullName + "*", false, false, false);

        public TypeShape GetFunctionPointerType(MethodSignature<TypeShape> signature) => new("fn*", false, false, false);

        public TypeShape GetGenericMethodParameter(GenericContext genericContext, int index) =>
            new($"!!{index}", false, false, false);

        public TypeShape GetGenericTypeParameter(GenericContext genericContext, int index) =>
            new($"!{index}", false, false, false);

        public TypeShape GetModifiedType(TypeShape modifier, TypeShape unmodifiedType, bool isRequired) => unmodifiedType;

        public TypeShape GetPinnedType(TypeShape elementType) => elementType;

        public TypeShape GetTypeFromSpecification(MetadataReader reader, GenericContext genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
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
                    if (op.Size == 1)
                    {
                        OneByte[raw] = op;
                    }
                    else if (op.Size == 2 && (raw >> 8) == 0xFE)
                    {
                        TwoByte[raw & 0xFF] = op;
                    }
                }
            }
        }
    }
}
