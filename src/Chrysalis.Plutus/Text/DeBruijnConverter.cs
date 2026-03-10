using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Text;

/// <summary>
/// Converts a <c>Program&lt;Name&gt;</c> (named variables) to <c>Program&lt;DeBruijn&gt;</c> (DeBruijn indices).
/// Ported from blaze-plutus convert.ts.
/// </summary>
internal sealed class DeBruijnConverter
{
    private int _currentLevel;
    private readonly List<BiMap> _levels = [new BiMap()];

    internal Program<DeBruijn> Convert(Program<Name> program)
    {
        return new(program.Version, ConvertTerm(program.Term));
    }

    private Term<DeBruijn> ConvertTerm(Term<Name> term)
    {
        return term switch
        {
            VarTerm<Name> v => new VarTerm<DeBruijn>(new DeBruijn(GetIndex(v.Name.Unique))),

            LambdaTerm<Name> lam => ConvertLambda(lam),

            ApplyTerm<Name> app => new ApplyTerm<DeBruijn>(
                ConvertTerm(app.Function),
                ConvertTerm(app.Argument)),

            DelayTerm<Name> delay => new DelayTerm<DeBruijn>(ConvertTerm(delay.Body)),

            ForceTerm<Name> force => new ForceTerm<DeBruijn>(ConvertTerm(force.Body)),

            ConstrTerm<Name> constr => new ConstrTerm<DeBruijn>(
                constr.Tag,
                [.. constr.Fields.Select(ConvertTerm)]),

            CaseTerm<Name> caseTerm => new CaseTerm<DeBruijn>(
                ConvertTerm(caseTerm.Scrutinee),
                [.. caseTerm.Branches.Select(ConvertTerm)]),

            ConstTerm<Name> con => new ConstTerm<DeBruijn>(con.Value),

            BuiltinTerm<Name> builtin => new BuiltinTerm<DeBruijn>(builtin.Function),

            ErrorTerm<Name> => new ErrorTerm<DeBruijn>(),

            _ => throw new ConvertException($"unknown term type: {term.GetType().Name}"),
        };
    }

    private Term<DeBruijn> ConvertLambda(LambdaTerm<Name> lam)
    {
        DeclareUnique(lam.Parameter.Unique);
        int paramIndex = GetIndex(lam.Parameter.Unique);
        StartScope();
        Term<DeBruijn> body = ConvertTerm(lam.Body);
        EndScope();
        RemoveUnique(lam.Parameter.Unique);
        return new LambdaTerm<DeBruijn>(new DeBruijn(paramIndex), body);
    }

    private void DeclareUnique(int unique)
    {
        _levels[_currentLevel].Insert(unique, _currentLevel);
    }

    private void RemoveUnique(int unique)
    {
        _levels[_currentLevel].Remove(unique);
    }

    private void StartScope()
    {
        _currentLevel++;
        _levels.Add(new BiMap());
    }

    private void EndScope()
    {
        _currentLevel--;
        _levels.RemoveAt(_levels.Count - 1);
    }

    private int GetIndex(int unique)
    {
        for (int i = _levels.Count - 1; i >= 0; i--)
        {
            int? level = _levels[i].GetLevel(unique);
            if (level.HasValue)
            {
                return _currentLevel - level.Value;
            }
        }
        throw new ConvertException($"free unique {unique}");
    }

    private sealed class BiMap
    {
        private readonly Dictionary<int, int> _uniqueToLevel = [];
        private readonly Dictionary<int, int> _levelToUnique = [];

        internal void Insert(int unique, int level)
        {
            _uniqueToLevel[unique] = level;
            _levelToUnique[level] = unique;
        }

        internal void Remove(int unique)
        {
            if (_uniqueToLevel.TryGetValue(unique, out int level))
            {
                _ = _levelToUnique.Remove(level);
            }
            _ = _uniqueToLevel.Remove(unique);
        }

        internal int? GetLevel(int unique)
        {
            return _uniqueToLevel.TryGetValue(unique, out int level) ? level : null;
        }
    }
}

/// <summary>
/// Thrown when DeBruijn index conversion fails (e.g., unbound variable).
/// </summary>
public sealed class ConvertException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ConvertException"/> class.</summary>
    public ConvertException() : base() { }

    /// <summary>Initializes a new instance of the <see cref="ConvertException"/> class with a message.</summary>
    public ConvertException(string message) : base(message) { }

    /// <summary>Initializes a new instance of the <see cref="ConvertException"/> class with a message and inner exception.</summary>
    public ConvertException(string message, Exception innerException) : base(message, innerException) { }
}
