# Execution budgets are enforced

`Evaluator.EvaluateScript`, `Evaluator.EvaluateTerm` and `CekMachine.Run` now stop a script that exceeds the budget you pass and throw `BudgetExceededException`, a subclass of `EvaluationException`. As on the node, a run is charged its startup, a budget equal to the cost passes, and one unit less in CPU or memory fails. Before, the budget was subtracted but never checked: the script returned a result, and a transaction declaring those units failed on chain.

Leaving `budget` out measures: the run is never stopped for budget and the result holds the units the script needs. `ExBudget.Unlimited` is now a budget like any other: a run fails with it only when a charge takes CPU or memory below zero. To measure, leave `budget` out; a measured part shown as the 64-bit maximum may have been clipped there. A script that never ends also never ends when measured, so pass a budget, such as the protocol's per-transaction maximum, for scripts you do not control.

## Before

```csharp
// Returned a result even when the script needed more.
var result = Evaluator.EvaluateScript(script, arguments, plutusV3Costs, budget: new ExBudget(Cpu: 1_000_000, Mem: 2_000));
```

## After

```csharp
try { var result = Evaluator.EvaluateScript(script, arguments, plutusV3Costs, budget: new ExBudget(Cpu: 1_000_000, Mem: 2_000)); }
catch (BudgetExceededException) { /* the script needs more than this budget */ }
```

A reused `CekMachine` charges startup on each run, from what earlier runs left. After a stop, `CekMachine.RemainingBudget` is the budget left when the machine stopped, negative in the part that ran out. When a builtin's cost is a stream on the node (for example, when measuring lists of 100 or more elements or sizes of `Data`), Chrysalis spends the whole cost at once, so the remaining budget can be lower than the node's.
