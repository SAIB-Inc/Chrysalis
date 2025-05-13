using Chrysalis.Tx.Models.Cbor;

namespace Chrysalis.Tx.Models;

public class InputOutputMapping
{
    private readonly Dictionary<string, (ulong InputIndex, Dictionary<string, ulong> OutputIndexes)> _inputOutputMapping = [];
    private readonly List<ResolvedInput> _resolvedInputs = [];
    private readonly Dictionary<string, ulong> _referenceInputs = [];

    public void AddInput(string inputId, ulong inputIndex)
    {
        if (!_inputOutputMapping.ContainsKey(inputId))
        {
            _inputOutputMapping[inputId] = (inputIndex, new Dictionary<string, ulong>());
        }
        else
        {
            var (_, outputs) = _inputOutputMapping[inputId];
            _inputOutputMapping[inputId] = (inputIndex, outputs);
        }
    }

    public void SetResolvedInputs(List<ResolvedInput> resolvedInputs)
    {
        List<ResolvedInput> sortedInputs = [.. resolvedInputs
        .OrderBy(x => Convert.ToHexString(x.Outref.TransactionId))
        .ThenBy(x => x.Outref.Index)];

        _resolvedInputs.AddRange(sortedInputs);
    }

    public List<ResolvedInput> GetResolvedInputs()
    {
        return _resolvedInputs;
    }
    public void AddReferenceInput(string inputId, ulong inputIndex)
    {
        _referenceInputs[inputId] = inputIndex;
    }

    public void AddOutput(string inputId, string outputId, ulong outputIndex)
    {
        if (!_inputOutputMapping.ContainsKey(inputId))
        {
            _inputOutputMapping[inputId] = (0, new Dictionary<string, ulong> { { outputId, outputIndex } });
        }
        else
        {
            var (inputIndex, outputs) = _inputOutputMapping[inputId];
            outputs[outputId] = outputIndex;
            _inputOutputMapping[inputId] = (inputIndex, outputs);
        }
    }

    public (ulong InputIndex, Dictionary<string, ulong> OutputIndexes) GetInput(string inputId)
    {
        return _inputOutputMapping.TryGetValue(inputId, out var value) ? value : (0, new Dictionary<string, ulong>());
    }

    public ulong GetReferenceInput(string refInputId)
    {
        return _referenceInputs.TryGetValue(refInputId, out var value) ? value : 0;
    }

    public Dictionary<string, (ulong InputIndex, Dictionary<string, ulong> OutputIndexes)> GetMappings()
    {
        return new Dictionary<string, (ulong InputIndex, Dictionary<string, ulong> OutputIndexes)>(_inputOutputMapping);
    }

    public IEnumerable<string> InputIds => _inputOutputMapping.Keys;

    public bool HasInput(string inputId) => _inputOutputMapping.ContainsKey(inputId);

    public bool HasOutput(string inputId, string outputId)
    {
        if (!_inputOutputMapping.TryGetValue(inputId, out var value)) return false;
        return value.OutputIndexes.ContainsKey(outputId);
    }
}
