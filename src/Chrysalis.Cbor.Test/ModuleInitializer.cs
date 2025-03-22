using System.Runtime.CompilerServices;
using VerifyTests;

namespace Chrysalis.Test;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifySourceGenerators.Initialize();
    }
}