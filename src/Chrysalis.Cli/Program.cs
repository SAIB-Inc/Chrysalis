using System;
using System.Text.Json;
using Chrysalis.Cbor.Extensions;
using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Governance;
using Chrysalis.Cbor.Types.Cardano.Core.Header;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cli;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Cbor.LocalStateQuery;

bool exitProgram = false;

NodeService nodeService = new("/tmp/preview-node.socket");
var options = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
};

while (!exitProgram)
{
    Console.WriteLine("===== Chrysalis =====");
    Console.WriteLine("1. Fetch Tip");
    Console.WriteLine("2. Fetch Protocol Parameters");
    Console.WriteLine("3. Fetch UTxOs by Address");
    Console.WriteLine("0. Exit");
    Console.Write("\nEnter your choice: ");

    // Get user input
    string input = Console.ReadLine() ?? "";

    switch (input)
    {
        case "1":
            Console.WriteLine("\n====================================================================\n");
            Tip tip = await nodeService.GetTipAsync();
            Console.Write($"Current tip: ");
            Console.Write(Convert.ToHexString(tip.Slot.Hash));
            Console.Write($" at slot ");
            Console.WriteLine(tip.Slot.Slot);
            Console.WriteLine("");

            break;
        case "2":
            ProtocolParams protocolParams = await nodeService.GetCurrentProtocolParamsAsync();
            var (
            minFeeA,
            minFeeB,
            maxBlockBodySize,
            maxTransactionSize,
            maxBlockHeaderSize,
            keyDeposit,
            poolDeposit,
            maximumEpoch,
            desiredNumberOfStakePools,
            poolPledgeInfluence,
            expansionRate,
            treasuryGrowthRate,
            protocolVersion,
            minPoolCost,
            adaPerUTxOByte,
            costModelsForScriptLanguage,
            executionCosts,
            maxTxExUnits,
            maxBlockExUnits,
            maxValueSize,
            collateralPercentage,
            maxCollateralInputs,
            poolVotingThresholds,
            drepVotingThresholds,
            minCommitteeSize,
            committeeTermLimit,
            governanceActionValidityPeriod,
            governanceActionDeposit,
            drepDeposit,
            drepInactivityPeriod,
            minFeeRefScriptCostPerByte
        ) = protocolParams;

            var propertyDict = new Dictionary<string, object?>
        {
            { nameof(ProtocolParams.MinFeeA), minFeeA },
            { nameof(ProtocolParams.MinFeeB), minFeeB },
            { nameof(ProtocolParams.MaxBlockBodySize), maxBlockBodySize },
            { nameof(ProtocolParams.MaxTransactionSize), maxTransactionSize },
            { nameof(ProtocolParams.MaxBlockHeaderSize), maxBlockHeaderSize },
            { nameof(ProtocolParams.KeyDeposit), keyDeposit },
            { nameof(ProtocolParams.PoolDeposit), poolDeposit },
            { nameof(ProtocolParams.MaximumEpoch), maximumEpoch },
            { nameof(ProtocolParams.DesiredNumberOfStakePools), desiredNumberOfStakePools },
            { nameof(ProtocolParams.PoolPledgeInfluence), poolPledgeInfluence },
            { nameof(ProtocolParams.ExpansionRate), expansionRate },
            { nameof(ProtocolParams.TreasuryGrowthRate), treasuryGrowthRate },
            { nameof(ProtocolParams.ProtocolVersion), protocolVersion },
            { nameof(ProtocolParams.MinPoolCost), minPoolCost },
            { nameof(ProtocolParams.AdaPerUTxOByte), adaPerUTxOByte },
            { nameof(ProtocolParams.CostModelsForScriptLanguage), costModelsForScriptLanguage },
            { nameof(ProtocolParams.ExecutionCosts), executionCosts },
            { nameof(ProtocolParams.MaxTxExUnits), maxTxExUnits },
            { nameof(ProtocolParams.MaxBlockExUnits), maxBlockExUnits },
            { nameof(ProtocolParams.MaxValueSize), maxValueSize },
            { nameof(ProtocolParams.CollateralPercentage), collateralPercentage },
            { nameof(ProtocolParams.MaxCollateralInputs), maxCollateralInputs },
            { nameof(ProtocolParams.PoolVotingThresholds), poolVotingThresholds },
            { nameof(ProtocolParams.DRepVotingThresholds), drepVotingThresholds },
            { nameof(ProtocolParams.MinCommitteeSize), minCommitteeSize },
            { nameof(ProtocolParams.CommitteeTermLimit), committeeTermLimit },
            { nameof(ProtocolParams.GovernanceActionValidityPeriod), governanceActionValidityPeriod },
            { nameof(ProtocolParams.GovernanceActionDeposit), governanceActionDeposit },
            { nameof(ProtocolParams.DRepDeposit), drepDeposit },
            { nameof(ProtocolParams.DRepInactivityPeriod), drepInactivityPeriod },
            { nameof(ProtocolParams.MinFeeRefScriptCostPerByte), minFeeRefScriptCostPerByte }
        };

            foreach (var kvp in propertyDict)
            {
                if (kvp.Value is PoolVotingThresholds || kvp.Value is DRepVotingThresholds)
                {
                    continue;
                }
                Console.Write($"{kvp.Key}: ");
                if (kvp.Value is CborRationalNumber rationalNumber)
                {
                    Console.WriteLine($"{(decimal)rationalNumber.Numerator / rationalNumber.Denominator}");
                    continue;
                }
                else if (kvp.Value is ProtocolVersion)
                {
                    continue;
                }
                else if (kvp.Value is CostMdls costMdls)
                {
                    foreach (var costMdl in costMdls.Value)
                    {
                        Console.WriteLine($"{costMdl.Key}: [");
                        foreach (var item in costMdl.Value.GetValue())
                        {
                            Console.WriteLine($"\t{item}, ");
                        }
                        Console.WriteLine("]");
                    }
                    continue;
                }
                else if (kvp.Value is ExUnitPrices exUnitPrices)
                {
                    Console.WriteLine($"Mem Price: {(decimal)exUnitPrices.MemPrice.Numerator / exUnitPrices.StepPrice.Denominator}, CPU Price: {(decimal)exUnitPrices.StepPrice.Numerator / exUnitPrices.StepPrice.Denominator}");
                    continue;
                }
                else if (kvp.Value is ExUnits exUnits)
                {
                    Console.WriteLine($"Mem: {exUnits.Mem}, CPU: {exUnits.Steps}");
                    continue;
                }


                Console.WriteLine(kvp.Value?.ToString() ?? "null");
                Console.ResetColor();
            }

            Console.WriteLine("\n\n");
            Console.ResetColor();


            break;

        case "3":

            Console.Write("Enter address: ");
            string address = Console.ReadLine() ?? "";

            Console.WriteLine($"Querying UTXOs for address: {address}");
            var utxos = await nodeService.GetUtxoByAddressAsync(address);
            Console.WriteLine("[");
            foreach (var utxo in utxos.Utxos)
            {
                Console.WriteLine(" {");
                Console.WriteLine("     transactionId: " + Convert.ToHexString(utxo.Key.TransactionId));
                Console.WriteLine("     index: " + utxo.Key.Index);
                Console.WriteLine("     amount: {");
                Console.WriteLine("         Lovelace: " + utxo.Value.Amount().Lovelace());
                if (utxo.Value.Amount() is LovelaceWithMultiAsset lovelaceWithMultiAsset)
                {
                    foreach (var asset in lovelaceWithMultiAsset.MultiAsset.Value)
                    {
                        Console.Write($"         {Convert.ToHexString(asset.Key)} : ");
                        Console.WriteLine("{");
                        foreach (var token in asset.Value.Value)
                        {
                            Console.WriteLine($"            {Convert.ToHexString(token.Key)} : {token.Value}");
                        }
                        Console.WriteLine("            }");
                    }
                }
                Console.WriteLine("     }");
                if (utxo.Value.DatumOption() is not null)
                {
                    Console.WriteLine("     datum: " + CborSerializer.Serialize(utxo.Value.DatumOption()!));
                }
                if (utxo.Value.ScriptRef() is not null)
                {
                    Console.WriteLine("     scriptRef: " + Convert.ToHexString(utxo.Value.ScriptRef()!));
                }

            }
            Console.WriteLine(" },");

            break;

        case "0":
            exitProgram = true;
            break;

        default:
            Console.WriteLine("Invalid option. Press any key to continue...");
            Console.ReadKey();
            break;
    }
}
