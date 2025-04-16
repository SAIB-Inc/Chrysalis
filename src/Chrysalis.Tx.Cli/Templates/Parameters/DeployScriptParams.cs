using Chrysalis.Tx.Cli.Templates.Models;
using Chrysalis.Tx.Models;

namespace Chrysalis.Tx.Cli.Templates.Parameters;
public record DeployScriptParameters(string ValidatorAddress,ScriptType ScriptType, string ScriptCborHex) : ITransactionParameters, ICommonTemplateParameters
{
    public Dictionary<string, (string address, bool isChange)> Parties { get; set; } = new() {
        { "validator", (ValidatorAddress, false) },
    };
}