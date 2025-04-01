namespace Chrysalis.Tx.Models;

public class InputOutputMapping
{
    private readonly Dictionary<string, (ulong InputIndex, Dictionary<string, ulong> OutputIndexes)> _mappings = new();

    public void AddInput(string inputId, ulong inputIndex)
    {
        if (!_mappings.ContainsKey(inputId))
        {
            _mappings[inputId] = (inputIndex, new Dictionary<string, ulong>());
        }
        else
        {
            var (_, outputs) = _mappings[inputId];
            _mappings[inputId] = (inputIndex, outputs);
        }
    }

    public void AddOutput(string inputId, string outputId, ulong outputIndex)
    {
        if (!_mappings.ContainsKey(inputId))
        {
            _mappings[inputId] = (0, new Dictionary<string, ulong> { { outputId, outputIndex } });
        }
        else
        {
            var (inputIndex, outputs) = _mappings[inputId];
            outputs[outputId] = outputIndex;
            _mappings[inputId] = (inputIndex, outputs);
        }
    }

    public (ulong InputIndex, Dictionary<string, ulong> OutputIndexes) GetInput(string inputId)
    {
        return _mappings.TryGetValue(inputId, out var value) ? value : (0, new Dictionary<string, ulong>());
    }

    public Dictionary<string, (ulong InputIndex, Dictionary<string, ulong> OutputIndexes)> GetMappings()
    {
        return new Dictionary<string, (ulong InputIndex, Dictionary<string, ulong> OutputIndexes)>(_mappings);
    }

    public IEnumerable<string> InputIds => _mappings.Keys;

    public bool HasInput(string inputId) => _mappings.ContainsKey(inputId);

    public bool HasOutput(string inputId, string outputId)
    {
        if (!_mappings.TryGetValue(inputId, out var value)) return false;
        return value.OutputIndexes.ContainsKey(outputId);
    }
}
