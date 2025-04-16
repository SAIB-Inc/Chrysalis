using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Cli.Templates.Models;
using Chrysalis.Tx.Cli.Templates.Parameters;
using Chrysalis.Tx.Models;

namespace Chrysalis.Tx.Cli.Templates;
public class CommonTemplates
{
    private readonly ICardanoDataProvider provider;
    private readonly Dictionary<string, Func<ICommonTemplateParameters, Task<Transaction>>> templates;
    private readonly string ChangeAddress;

    public CommonTemplates(ICardanoDataProvider provider, string bech32ChangeAddress)
    {
        this.provider = provider;
        templates = [];
        ChangeAddress = bech32ChangeAddress;
        InitializeTemplates();
    }

    private void InitializeTemplates()
    {
        var deployScript = TransactionTemplateBuilder<DeployScriptParameters>.Create(provider)
            .AddStaticParty("change", ChangeAddress, true)
            .AddInput((options, parameters) =>
            {
                options.From = "change";
            })
            .AddOutput((options, parameters) =>
            {
                options.To = "validator";
                options.Amount = new Lovelace(2000000UL);
                options.Script = parameters.ScriptType switch
                {
                    ScriptType.PlutusV1 => new PlutusV1Script(new Value1(1), Convert.FromHexString(parameters.ScriptCborHex)),
                    ScriptType.PlutusV2 => new PlutusV2Script(new Value2(2), Convert.FromHexString(parameters.ScriptCborHex)),
                    ScriptType.PlutusV3 => new PlutusV3Script(new Value3(3), Convert.FromHexString(parameters.ScriptCborHex)),
                    _ => throw new ArgumentException("Invalid script type")
                };
            })
            .Build();

        templates.Add("deployScript", async (parameters) =>
        {
            if (parameters is not DeployScriptParameters deployParams)
                throw new ArgumentException("Invalid parameter type for deployScript template");

            return await deployScript(deployParams);
        });

    }
    public Func<DeployScriptParameters, Task<Transaction>> DeployScript =>
        (deployParams) => templates["deployScript"](deployParams);
}       