# Motif

**Motif** is an **F# DSL / authoring layer** on top of **Microsoft Agent Framework**.

Canonical boundary:

> Motif describes. Interpreters materialize. Backends execute.

Motif is not a standalone platform, runtime, scheduler, memory system, cloud abstraction, or replacement for Microsoft Agent Framework.

## Current status

Early-stage .NET 10 spike focused on **fast F# authoring for MAF**:

- `Motif.Core`
  - lightweight `agent "name" { ... }` computation expression
  - `AgentSpec`
  - `ToolRef`
  - `OutputSpec`
  - validation
- `Motif.AgentFramework`
  - thin adapter from `AgentSpec` to native MAF `AIAgent`
  - F# facade: `spec |> Maf.agent client`
- `Motif.Tests`
  - DSL tests
  - validation tests
  - adapter tests with fake `IChatClient`

The experimental `MotifProgram<'T>` / `TestInterpreter` files still exist from earlier spikes, but the product direction is now simpler: make F# authoring of MAF agents/programs fast and pleasant first.

## Simple F# authoring DSL

```fsharp
let quoteTool =
    Tool.ofSyncFunc "quote" "Return a mock quote" (fun (ticker: string) -> $"quote:{ticker}")
    |> Result.defaultWith failwith

let trader =
    agent "trader" {
        instructions "You are a concise trading assistant. Return Buy, Sell, or Hold."
        tool quoteTool
        output (Output.dotNetType<Decision> ())
        metadata "style" "fast-maf-authoring"
    }
```

The point is not to create a second runtime. Motif should make MAF programs quicker to write, while `Motif.AgentFramework` materializes specs into native MAF objects.

```fsharp
let mafAgent =
    trader |> Maf.agent client
```

`Maf.agent` is just F#-friendly conversion sugar over `Adapter.toAgent`; native MAF still owns execution.

## Experimental Program slice

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

A debate is still just description in Core. The deterministic interpreter evaluates participants first for fixture coverage and returns the judge result:

```fsharp
let debateProgram : MotifProgram<Decision> =
    Program.debate 2
        [ Program.run<Ticker, DebateArgument> bullResearcher (Ticker "NVDA")
          Program.run<Ticker, DebateArgument> bearResearcher (Ticker "NVDA") ]
        (Program.run<DebateArgument list, Decision> researchManager [])
```

Examples:

```text
examples/05-program-test-interpreter/Motif.fsx
examples/06-debate-test-interpreter/Motif.fsx
examples/07-route-quotation/Motif.fsx
examples/08-simple-maf-dsl/Motif.fsx
examples/09-maf-agent-facade/Motif.fsx
```

Routes use quoted predicates so conditions remain inspectable instead of opaque runtime continuations:

```fsharp
let hasEnoughReports =
    Predicate.quote <@ fun (reports: MarketReport list) -> reports.Length > 1 @>

let decision =
    Program.route analystReports hasEnoughReports trade fallback
```

The first `TestInterpreter` subset supports simple quoted property access and comparison expressions such as `reports.Length > 1`.

## Build and test

Use the local .NET 10 SDK path in this environment:

```bash
/opt/data/dotnet/dotnet test Motif.sln --nologo
```

Expected current result:

```text
Passed: 22, Failed: 0
```

## Design docs

See:

```text
docs/MOTIF_SPEC.md
```
