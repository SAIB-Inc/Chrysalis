using Chrysalis.Plutus.VM.EvalTx;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Serialization;

Console.WriteLine("PlutusVM.Net Script Parameter Application Test");
Console.WriteLine("==============================================");

// Test script parameter application with real data
string scriptHex = "5860010100229800aba2aba1aab9eaab9dab9a9bae00248888896600264653001300700198039804000cc01c0092225980099b8748008c020dd500144c8cc892898058009805980600098049baa0028b200e180380098021baa0078a4d1365640081";
string parameterHex = "581CF4C9F9C4252D86702C2F4C2E49E6648C7CFFE3C8F2B6B7D779788F50";

Console.WriteLine($"Original script: {scriptHex}");
Console.WriteLine($"Parameter (hex): {parameterHex}");

try 
{
    // Deserialize parameter from CBOR
    byte[] parameterBytes = Convert.FromHexString(parameterHex);
    PlutusData parameter = CborSerializer.Deserialize<PlutusData>(parameterBytes);
    
    Console.WriteLine($"Parameter deserialized successfully: {parameter.GetType().Name}");

    // Apply parameter to the script
    Console.WriteLine("\nApplying parameter to script...");
    string parameterizedScript = ScriptApplicator.ApplyParameters(scriptHex, parameter);
    
    Console.WriteLine($"✓ Script parameterization successful!");
    Console.WriteLine($"Parameterized script: {parameterizedScript}");
    Console.WriteLine($"Length change: {scriptHex.Length} -> {parameterizedScript.Length} characters");
}
catch (Exception ex)
{
    Console.WriteLine($"✗ Error: {ex.Message}");
    if (ex.InnerException != null)
    {
        Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
    }
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
}