using System.Runtime.CompilerServices;

namespace Chrysalis.Codec.Test;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifySourceGenerators.Initialize();
    }
}