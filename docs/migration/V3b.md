# Scripts with an unbound variable are refused

`Evaluator.EvaluateScript` now refuses a script that refers to a variable no enclosing lambda binds, before running it, and throws `UnboundVariableException`, a subclass of `EvaluationException`. The node refuses such a script even when the variable is never reached, for example under a `delay` that is never forced or in a lambda that is never applied. Before, Chrysalis ran it and could return a result, and a transaction carrying it failed on chain. As on the node, variables inside `constr` and `case` are not checked before running: one there fails with `EvaluationException` only if it is evaluated. `Evaluator.EvaluateTerm` and `CekMachine` do not check, as before.

## Before

    var result = Evaluator.EvaluateScript(script, arguments, plutusVersion: 3, protocolVersion: 11, plutusV3Costs); // Returned a result for a script the chain refuses.

## After

    try { var result = Evaluator.EvaluateScript(script, arguments, plutusVersion: 3, protocolVersion: 11, plutusV3Costs); }
    catch (UnboundVariableException) { /* the script refers to a variable no lambda binds */ }
