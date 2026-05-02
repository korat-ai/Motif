# Motif Spike

Disposable spike for the first Motif primitives.

## Boundary

Motif is a records-first F# pre-runtime authoring layer over Microsoft Agent Framework.

Motif v0 does:

- define `AgentSpec`
- define `ToolRef`
- define `OutputSpec`
- validate obvious pre-runtime mistakes
- prepare for an explicit `Motif.Maf.toAgent` adapter

Motif v0 does not:

- run agents
- stream responses
- manage `AgentThread`
- wrap `AgentResponse`
- define workflows
- create a scheduler
- create memory/runtime/cloud abstractions
- replace Microsoft Agent Framework

## First primitives

```fsharp
let searchTool =
    Tool.ofSyncFunc "search" "Search documents" (fun query -> $"Results for {query}")

let spec =
    Agent.unsafeCreate "research-agent"
    |> Agent.withInstructions "Answer using the search tool when needed."
    |> Agent.withTool (searchTool |> Result.defaultWith failwith)

let validation = Validation.validate spec
```

## Proof gates before real v1

Continue only if the spike proves:

- Motif examples are at least 30% shorter than raw MAF in 2 of 3 cases.
- Validation catches at least 3 useful errors.
- The future `Motif.Maf.toAgent` adapter stays tiny and obvious.
- Raw MAF escape hatches work without rewriting `AgentSpec`.
- No workflow/run/stream/runtime abstraction creeps into core.
