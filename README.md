# Motif

**Motif** is an **F# DSL / authoring layer** on top of **Microsoft Agent Framework**.

Canonical boundary:

> Motif describes. Interpreters materialize. Backends execute.

Motif is not a standalone platform, runtime, scheduler, memory system, cloud abstraction, or replacement for Microsoft Agent Framework.

## Current status

Early-stage .NET 10 spike with:

- `Motif.Core`
  - `AgentSpec`
  - `ToolRef`
  - `OutputSpec`
  - validation
  - `MotifProgram<'T>` initial/free-like program description
  - deterministic `TestInterpreter`
- `Motif.AgentFramework`
  - thin adapter from `AgentSpec` to native MAF `AIAgent`
- `Motif.Tests`
  - validation tests
  - adapter tests with fake `IChatClient`
  - program/test-interpreter tests

## Program slice

Agents are static capability blocks:

```fsharp
let marketAnalyst =
    Agent.unsafeCreate "market-analyst"
    |> Agent.withInstructions "Produce a concise market report."
```

Programs describe operations over agents without executing models or tools:

```fsharp
let program : MotifProgram<Decision> =
    Program.sequence
        (Program.fanout [
            Program.run<Ticker, MarketReport> marketAnalyst (Ticker "NVDA")
            Program.run<Ticker, MarketReport> newsAnalyst (Ticker "NVDA")
        ])
        (Program.run<MarketReport list, Decision> trader [ MarketReport "placeholder" ])
```

The test interpreter is deterministic and fixture-based:

```fsharp
let interpreter =
    TestInterpreter.empty
    |> TestInterpreter.withAgentResult<MarketReport> "market-analyst" (MarketReport "technical trend is positive")
    |> TestInterpreter.withAgentResult<Decision> "trader" Buy

let result = TestInterpreter.run program interpreter
```

## Build and test

Use the local .NET 10 SDK path in this environment:

```bash
/opt/data/dotnet/dotnet test Motif.sln --nologo
```

Expected current result:

```text
Passed: 13, Failed: 0
```

## Design docs

See:

```text
docs/MOTIF_SPEC.md
```
