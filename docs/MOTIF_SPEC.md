# Motif Specification

**Status:** draft product/technical specification  
**Audience:** implementers of Motif.Core, Motif.AgentFramework, and future adapters  
**Canonical phrase:** Motif describes agent systems. Backends materialize and execute them.

---

## 1. Purpose

Motif is an F# authoring layer for building multi-agent systems quickly, canonically, and with strong pre-runtime validation.

The motivating target is the ability to express systems similar to `TauricResearch/TradingAgents` in one compact F# file or from reusable agent libraries:

- analyst agents gather market, fundamental, news, and sentiment evidence;
- researcher agents debate bullish and bearish interpretations;
- a manager/judge agent synthesizes the debate;
- a trader agent turns the thesis into a trade plan;
- risk agents debate the trade plan;
- a portfolio manager produces the final decision.

Motif should let users describe this system as typed F# values and programs, then choose an interpreter/backend such as Microsoft Agent Framework, Semantic Kernel, AutoGen.NET, BotSharp, or deterministic test execution.

Motif is not an agent runtime. It is an authoring language plus a backend-interpreter boundary.

---

## 2. Core Principles

1. **Description before execution**
   - Motif.Core builds immutable descriptions: agents, tools, outputs, flows, debates, and programs.
   - Motif.Core does not call LLMs, stream responses, manage threads, schedule work, or store checkpoints.

2. **Agents are static capability blocks**
   - An agent describes role, instructions, tools, output contract, and metadata.
   - An agent is not a monad and not an effect.

3. **Programs are free-monad-like descriptions of agentic effects**
   - Running an agent, fanout, debate, collect, route, and judge are operations in a Motif program.
   - A Motif program is an initial/free representation that can be interpreted by different backends.

4. **Interpreters own materialization and execution semantics**
   - Motif.AgentFramework materializes Motif descriptions into native Microsoft Agent Framework objects.
   - Future adapters may target Semantic Kernel Agents, AutoGen.NET, BotSharp, Elsa, test execution, diagrams, or documentation.

5. **No hidden runtime**
   - Motif must not wrap `RunAsync`, `RunStreamingAsync`, `AgentThread`, `AgentResponse`, checkpointing, retries, memory stores, model clients, or provider configuration in Core.

6. **Escape hatches are first-class**
   - Advanced native framework objects should be passable through adapter-specific/raw values.
   - Raw escape hatches must fail explicitly if unsupported by a chosen interpreter.

7. **F# first, not Python-port style**
   - Prefer discriminated unions, records, typed builders, computation expressions, and composable functions.
   - Avoid copying LangGraph or LangChain surface area directly.

---

## 3. Package Layout

### 3.1 Motif.Core

Pure F# primitives and validation. No dependency on Microsoft Agent Framework, Semantic Kernel, AutoGen, BotSharp, or provider packages.

Contains:

- names and identifiers;
- `AgentSpec`;
- `ToolRef` and tool specs;
- `OutputSpec`;
- system/team specs;
- flow specs;
- debate specs;
- Motif program algebra;
- builders/combinators;
- validation;
- interpreter capability model.

### 3.2 Motif.AgentFramework

Adapter for Microsoft Agent Framework.

Contains:

- `AgentSpec -> Microsoft.Agents.AI.AIAgent`;
- `ToolRef -> Microsoft.Extensions.AI.AITool`;
- later: `MotifProgram<'T> -> MAF WorkflowBuilder` or equivalent native workflow object;
- native MAF options passthrough;
- materialization errors.

Current verified dependencies:

```xml
<PackageReference Include="Microsoft.Agents.AI" Version="1.3.0" />
<PackageReference Include="Microsoft.Extensions.AI" Version="10.5.1" />
```

### 3.3 Future adapters

Possible later packages:

- `Motif.SemanticKernel`
- `Motif.AutoGen`
- `Motif.BotSharp`
- `Motif.Elsa`
- `Motif.Testing`
- `Motif.Diagrams`
- `Motif.Docs`

---

## 4. Non-Goals

Motif.Core must not implement:

- LLM provider abstraction;
- execution lifecycle;
- streaming runtime;
- agent threads;
- response objects;
- scheduling;
- checkpointing;
- long-term memory store;
- retries/backoff;
- human approval runtime;
- cloud deployment;
- tracing backend;
- workflow runtime;
- proprietary replacement for MAF/SK/AutoGen/BotSharp.

Motif may describe concepts that interpreters can materialize, but Core must not execute them.

---

## 5. Existing v0 Foundation

The current spike already contains:

```text
src/Motif.Core/Primitives.fs
src/Motif.Core/Agent.fs
src/Motif.Core/Tool.fs
src/Motif.Core/Output.fs
src/Motif.Core/Validation.fs
src/Motif.AgentFramework/Adapter.fs
tests/Motif.Tests/ValidationTests.fs
tests/Motif.Tests/AdapterTests.fs
examples/04-trading-agents-style/Motif.fsx
```

Current verified behavior:

- `AgentSpec` can be created and validated.
- Function tools can be represented with boxed `System.Func` delegates.
- Raw tools can pass through if already native `AITool` values.
- `Adapter.toAgent` can materialize a native `ChatClientAgent`/`AIAgent` using a fake `IChatClient`.
- Full tests passed with `7` passing tests on `net10.0`.

---

## 6. Primitive Types

### 6.1 Names

Names should be opaque value types with validation.

```fsharp
type AgentName = private AgentName of string
type ToolName = private ToolName of string
type SystemName = private SystemName of string
type StepName = private StepName of string
type DebateName = private DebateName of string
```

Each name module should expose:

```fsharp
val create : string -> Result<NameType, string>
val value : NameType -> string
```

Validation rules:

- cannot be null, empty, or whitespace;
- should be stable for logs and graph node IDs;
- should reject or normalize dangerous characters only if required by an interpreter;
- interpreter-specific naming constraints should be reported by interpreter validation, not forced globally unless universal.

---

## 7. Agent Model

### 7.1 AgentSpec

An agent is a static description of a role/capability.

```fsharp
type AgentSpec =
    { Name: AgentName
      Instructions: string option
      Tools: ToolRef list
      Output: OutputSpec option
      Metadata: Map<string, string> }
```

Meaning:

- `Name`: stable identifier for materialization, logs, diagrams, and validation.
- `Instructions`: role and behavior prompt/instructions.
- `Tools`: callable capabilities exposed to the agent.
- `Output`: desired output contract/metadata.
- `Metadata`: generic descriptive metadata such as `description`, `role`, `team`, or tags.

### 7.2 Agent builder module

Records-first API:

```fsharp
module Agent =
    val create : string -> Result<AgentSpec, string>
    val unsafeCreate : string -> AgentSpec
    val withInstructions : string -> AgentSpec -> AgentSpec
    val withTool : ToolRef -> AgentSpec -> AgentSpec
    val withTools : ToolRef list -> AgentSpec -> AgentSpec
    val withOutput : OutputSpec -> AgentSpec -> AgentSpec
    val withMetadata : string -> string -> AgentSpec -> AgentSpec
```

### 7.3 Agent computation expression

A convenient CE may be added after records-first primitives remain stable.

Target usage:

```fsharp
let marketAnalyst =
    agent "market-analyst" {
        instructions """
        Produce a technical market report.
        Focus on trend, momentum, volatility, volume, and indicators.
        Do not make final Buy/Sell/Hold decisions.
        """

        uses marketData
        uses technicalIndicators

        output<MarketReport>
        metadata "role" "analyst"
    }
```

Important: this CE only builds `AgentSpec`. It is not a free monad and does not execute anything.

---

## 8. Typed Agent Facade

Motif should support a typed facade over `AgentSpec` for better F# composition.

```fsharp
type Agent<'input, 'output> =
    { Spec: AgentSpec }
```

Target usage:

```fsharp
let marketAnalyst : Agent<Ticker, MarketReport> =
    agent<Ticker, MarketReport> "market-analyst" {
        instructions "Analyze market data and produce a market report."
        uses marketData
        output<MarketReport>
    }
```

The typed facade should not duplicate `AgentSpec`; it should wrap it.

Benefits:

- `run : Agent<'input, 'output> -> 'input -> Motif<'output>` can be type-safe.
- Composition errors become compile-time errors where possible.
- Adapters can still materialize the underlying untyped `AgentSpec`.

MVP may start with untyped `AgentSpec`, then add `Agent<'input,'output>` once flow/program API is ready.

---

## 9. Tool Model

### 9.1 ToolRef

```fsharp
type ToolRef =
    | FunctionTool of FuncToolSpec
    | NativeTool of RawTool
```

### 9.2 Function tools

```fsharp
type FuncToolSpec =
    { Name: ToolName
      Description: string option
      Handler: obj
      InputType: Type option
      OutputType: Type option }
```

The handler should be stored as a `System.Delegate`/`System.Func` shape, not as an opaque F# function object, because MAF `AIFunctionFactory.Create` consumes delegates.

Supported initial function shapes:

```fsharp
'Input -> Task<'Output>
'Input -> Async<'Output>
'Input -> 'Output
unit -> Task<'Output>
unit -> Async<'Output>
unit -> 'Output
```

Multi-field inputs should use a single record input type, not curried arguments.

Rejected initial shapes:

```fsharp
'a -> 'b -> 'c
'a * 'b -> 'c      // optional later, but avoid in v1
obj -> obj         // unless explicitly raw/unsafe
```

### 9.3 Raw/native tools

```fsharp
type RawTool =
    { Name: ToolName
      Value: obj }
```

Rules:

- Core only stores raw value.
- An interpreter decides whether it can materialize the raw value.
- MAF adapter currently accepts raw values only if they are already `Microsoft.Extensions.AI.AITool`.
- Unsupported raw values must produce explicit materialization errors.

### 9.4 Tool builder module

```fsharp
module Tool =
    val raw : name:string -> value:obj -> Result<ToolRef, string>
    val ofFunc : name:string -> description:string -> ('input -> Task<'output>) -> Result<ToolRef, string>
    val ofAsyncFunc : name:string -> description:string -> ('input -> Async<'output>) -> Result<ToolRef, string>
    val ofSyncFunc : name:string -> description:string -> ('input -> 'output) -> Result<ToolRef, string>
    val ofUnitFunc : name:string -> description:string -> (unit -> 'output) -> Result<ToolRef, string>
    val ofUnitTaskFunc : name:string -> description:string -> (unit -> Task<'output>) -> Result<ToolRef, string>
```

---

## 10. Output Model

```fsharp
type OutputSpec =
    | JsonSchema of name: string * schema: string
    | DotNetType of Type
    | RawOutput of obj
```

Builder module:

```fsharp
module Output =
    val jsonSchema : name:string -> schema:string -> OutputSpec
    val dotNetType<'T> : unit -> OutputSpec
    val raw : obj -> OutputSpec
```

Rules:

- `OutputSpec` is initially metadata/contract.
- It must not become a serialization framework in Core.
- Adapters may map output specs to native structured-output features where available.
- Unsupported output specs should produce capability/materialization warnings or errors depending on strictness.

---

## 11. System and Flow Model

### 11.1 SystemSpec

A system groups agents and an optional flow/program.

```fsharp
type SystemSpec =
    { Name: SystemName
      Agents: AgentSpec list
      Flow: FlowSpec option
      Metadata: Map<string, string> }
```

The name `SystemSpec` is preferred over `AgentSystemSpec` because systems may contain agents, flows, debates, output contracts, policies, and metadata.

### 11.2 FlowSpec

`FlowSpec` is a structural graph/flow description. It is not a runtime.

```fsharp
type FlowSpec =
    | Step of FlowStep
    | Sequence of FlowStep list
    | Fanout of FlowStep list
    | Debate of DebateSpec
```

### 11.3 FlowStep

```fsharp
type FlowStep =
    | AgentStep of AgentSpec
    | NamedStep of StepName * FlowStep
    | NestedFlow of FlowSpec
```

Rationale:

- `Sequence` and `Fanout` can contain agents or nested flows.
- Later concepts such as `Collect`, `Route`, `Transform`, and `HumanApproval` can be introduced without breaking the model.

### 11.4 DebateSpec

```fsharp
type DebateSpec =
    { Name: DebateName
      Participants: AgentSpec list
      Judge: AgentSpec
      MaxRounds: int
      Strategy: DebateStrategy option
      Metadata: Map<string, string> }
```

```fsharp
type DebateStrategy =
    | RoundRobin
    | Pairwise
    | JudgeAfterEachRound
    | JudgeAtEnd
```

Rules:

- A debate must have at least one participant.
- A debate must have a judge.
- `MaxRounds` must be positive.
- The debate description does not execute rounds; interpreters decide how to map it.

---

## 12. Free-Monad-Like Program Model

### 12.1 Design decision

Agents should not be free monads. Agents are static capability blocks.

The free-monad-like structure belongs to the program/flow layer, where operations describe agentic effects.

```text
AgentSpec = who the agent is and what it can do
Motif<'T> = what the system does with agents
Interpreter = how a backend materializes or executes that program
```

### 12.2 Conceptual algebra

A Motif program describes operations such as:

- run an agent;
- fan out branches;
- run a debate;
- collect values;
- judge/synthesize;
- return a result.

Conceptual shape:

```fsharp
type Motif<'a> =
    | Pure of 'a
    | Free of MotifOp<Motif<'a>>

and MotifOp<'next> =
    | RunAgent of AgentSpec * inputType: Type * outputType: Type * next: 'next
    | Fanout of ProgramNode list * next: 'next
    | Debate of DebateSpec * next: 'next
    | Collect of name: string * next: 'next
```

This is a conceptual model, not necessarily the exact first implementation.

### 12.3 Pragmatic first implementation

A simpler AST may be better for v1:

```fsharp
type ProgramNode =
    | AgentRun of step: StepName option * agent: AgentSpec * inputType: Type option * outputType: Type option
    | SequenceNode of step: StepName option * steps: ProgramNode list
    | FanoutNode of step: StepName option * branches: ProgramNode list
    | DebateNode of step: StepName option * debate: DebateSpec
    | CollectNode of step: StepName option * name: string
    | ReturnNode of outputType: Type option

type MotifProgram<'output> =
    { Root: ProgramNode
      OutputType: Type
      Metadata: Map<string, string> }
```

This is easier to validate and materialize than a fully generic free monad, while preserving the same idea.

### 12.4 Motif computation expression

Target usage:

```fsharp
let tradingAgents =
    motif {
        let! analystReports =
            fanout [
                run marketAnalyst ticker
                run fundamentalsAnalyst ticker
                run newsAnalyst ticker
                run sentimentAnalyst ticker
            ]

        let! thesis =
            runDebate researchDebate analystReports

        let! tradePlan =
            run trader thesis

        let! finalDecision =
            runDebate riskDebate tradePlan

        return finalDecision
    }
```

Important:

- This builds `MotifProgram<Decision>`.
- It does not execute LLM calls.
- `run`, `fanout`, and `runDebate` are descriptive operations.
- Interpreter packages decide how to materialize or execute.

### 12.5 Typed run API

Typed facade target:

```fsharp
val run : Agent<'input, 'output> -> 'input -> Motif<'output>
val fanout : Motif<'a> list -> Motif<'a list>
val runDebate : Debate<'input, 'output> -> 'input -> Motif<'output>
```

Untyped MVP fallback:

```fsharp
val run : AgentSpec -> input:obj -> Motif<obj>
val fanout : Motif<obj> list -> Motif<obj list>
val runDebate : DebateSpec -> input:obj -> Motif<obj>
```

Recommendation:

- implement untyped internal AST first;
- add typed wrappers where they improve authoring;
- avoid overbuilding a type-level workflow engine before adapter proof.

---

## 13. TradingAgents-Style Target Example

### 13.1 Agents

```fsharp
let marketAnalyst =
    agent<Ticker, MarketReport> "market-analyst" {
        instructions "Produce a concise technical-analysis report. Do not make final decisions."
        uses marketData
        uses technicalIndicators
        output<MarketReport>
    }

let fundamentalsAnalyst =
    agent<Ticker, FundamentalsReport> "fundamentals-analyst" {
        instructions "Evaluate financial health, valuation, growth, and red flags."
        uses fundamentals
        output<FundamentalsReport>
    }

let newsAnalyst =
    agent<Ticker, NewsReport> "news-analyst" {
        instructions "Summarize market-moving news and macro context."
        uses news
        output<NewsReport>
    }

let sentimentAnalyst =
    agent<Ticker, SentimentReport> "sentiment-analyst" {
        instructions "Summarize social and public sentiment."
        uses sentiment
        output<SentimentReport>
    }
```

### 13.2 Debates

```fsharp
let researchDebate =
    debate<AnalystReports, ResearchThesis> "research-debate" {
        rounds 2
        participants [ bullResearcher; bearResearcher ]
        judge researchManager
        strategy JudgeAtEnd
        output<ResearchThesis>
    }

let riskDebate =
    debate<TradePlan, FinalDecision> "risk-debate" {
        rounds 2
        participants [ aggressiveRisk; conservativeRisk; neutralRisk ]
        judge portfolioManager
        strategy JudgeAtEnd
        output<FinalDecision>
    }
```

### 13.3 Program

```fsharp
let tradingAgents ticker =
    motif {
        let! analystReports =
            fanout [
                run marketAnalyst ticker
                run fundamentalsAnalyst ticker
                run newsAnalyst ticker
                run sentimentAnalyst ticker
            ]

        let! thesis =
            runDebate researchDebate analystReports

        let! tradePlan =
            run trader thesis

        let! decision =
            runDebate riskDebate tradePlan

        return decision
    }
```

### 13.4 Materialization

```fsharp
let mafWorkflow =
    tradingAgents (Ticker "NVDA")
    |> Motif.AgentFramework.Workflow.materialize options
```

or, for testing:

```fsharp
let trace =
    tradingAgents (Ticker "NVDA")
    |> Motif.Testing.Interpreter.run fixtures
```

or, for docs:

```fsharp
let diagram =
    tradingAgents (Ticker "NVDA")
    |> Motif.Diagrams.Mermaid.render
```

---

## 14. Interpreter Model

### 14.1 Interpreter responsibilities

An interpreter may:

- validate whether it supports all nodes/tools/output specs;
- materialize Motif agents to native backend agents;
- materialize flows/programs to native backend workflow/group-chat objects;
- provide backend-specific options;
- report unsupported operations clearly.

An interpreter must not force Core to adopt backend-specific concepts.

### 14.2 Capability model

Each interpreter should expose capabilities:

```fsharp
type InterpreterCapabilities =
    { SupportsFanout: bool
      SupportsDebate: bool
      SupportsTypedOutput: bool
      SupportsNativeTools: bool
      SupportsCheckpointing: bool
      SupportsStreaming: bool
      SupportedToolShapes: ToolShape list }
```

Core validation is universal. Interpreter validation is capability-specific.

### 14.3 Validation modes

```fsharp
type ValidationMode =
    | Permissive
    | Strict
```

- `Permissive`: warnings for unsupported metadata/output features.
- `Strict`: errors for anything not materializable by the selected interpreter.

---

## 15. Motif.AgentFramework Specification

### 15.1 Current agent materialization

Current adapter API:

```fsharp
module Motif.AgentFramework.Adapter =
    val toolToAiTool : ToolRef -> Result<Microsoft.Extensions.AI.AITool, AdapterError>
    val toAgent : Microsoft.Extensions.AI.IChatClient -> AgentSpec -> Result<Microsoft.Agents.AI.AIAgent, AdapterError list>
```

Current behavior:

- validate `AgentSpec`;
- convert function tools with `AIFunctionFactory.Create`;
- pass raw tools through only if they are already `AITool`;
- call `IChatClient.AsAIAgent(...)`;
- return native `AIAgent`.

### 15.2 MAF options passthrough

MAF exposes `ChatClientAgentOptions.ChatOptions` and an `AsAIAgent` overload accepting `ChatClientAgentOptions`.

Motif should not invent a Core abstraction over `ChatOptions` in v1.

Preferred adapter-level options:

```fsharp
type AgentFrameworkMaterializationOptions =
    { AgentOptions: Microsoft.Agents.AI.ChatClientAgentOptions option
      LoggerFactory: Microsoft.Extensions.Logging.ILoggerFactory option
      Services: IServiceProvider option
      ValidationMode: ValidationMode }
```

Potential API:

```fsharp
module Adapter =
    val toAgent : IChatClient -> AgentSpec -> Result<AIAgent, AdapterError list>
    val toAgentWithOptions : IChatClient -> AgentFrameworkMaterializationOptions -> AgentSpec -> Result<AIAgent, AdapterError list>
```

### 15.3 Future workflow materialization

Target:

```fsharp
module Workflow =
    val materialize : AgentFrameworkWorkflowOptions -> MotifProgram<'output> -> Result<NativeWorkflow, AdapterError list>
```

The exact MAF native workflow type must be verified against the package version before implementation.

Rules:

- Do not create Motif runtime wrappers.
- Return native MAF workflow/builder objects or a thin materialization result.
- If a Motif operation cannot be mapped cleanly to MAF, fail explicitly.

---

## 16. Future Adapter Notes

### 16.1 Semantic Kernel

Likely package: `Motif.SemanticKernel`.

Use cases:

- SK agents;
- SK plugins/functions;
- connector ecosystem;
- RAG/memory-oriented applications.

Risk:

- Motif must not become a Semantic Kernel wrapper.

### 16.2 AutoGen.NET

Likely package: `Motif.AutoGen`.

Use cases:

- group chat;
- debate;
- manager/judge patterns.

Risk:

- .NET AutoGen maturity must be proven before committing.

### 16.3 BotSharp

Likely package: `Motif.BotSharp`.

Use cases:

- .NET-native multi-agent framework;
- routing, plugins, knowledge, conversation workflows.

Risk:

- may be too platform-heavy for Motif's thin authoring philosophy.

### 16.4 Elsa

Likely package: `Motif.Elsa`.

Use cases:

- durable workflow execution;
- long-running flows;
- visual workflow designer.

Risk:

- not an agent framework; agent semantics must be built or adapted.

### 16.5 Testing, diagrams, docs

These are high-value early interpreters:

- `Motif.Testing`: deterministic execution with fixtures/fake agent outputs;
- `Motif.Diagrams`: Mermaid/Graphviz rendering;
- `Motif.Docs`: Markdown architecture documentation.

These interpreters prove that Motif is a useful IR, not only a MAF convenience layer.

---

## 17. Validation Specification

### 17.1 Universal validation

Core validation should catch:

- missing/empty agent names;
- missing instructions;
- duplicate tool names within an agent;
- unsupported function tool shapes;
- invalid empty JSON schema;
- empty system name;
- duplicate agent names within a system;
- flow steps referencing duplicate names;
- empty sequence;
- empty fanout;
- debate without participants;
- debate without judge;
- debate with `MaxRounds <= 0`;
- debate participant also serving as judge, if disallowed by strict policy;
- output type missing where a typed flow expects one.

### 17.2 Interpreter validation

Interpreter validation should catch:

- raw tool unsupported by backend;
- output spec unsupported by backend;
- operation unsupported by backend;
- native naming constraints;
- tool shape unsupported by backend;
- debate strategy unsupported by backend;
- fanout/parallelism unsupported or not representable;
- native options inconsistent with Motif spec.

### 17.3 Error style

Errors must be actionable.

Bad:

```text
Invalid tool.
```

Good:

```text
Tool 'lookup-price' uses a curried function shape ('a -> 'b -> 'c'). Motif v1 supports single-input function tools only. Use a record input type instead.
```

---

## 18. Testing Strategy

### 18.1 Unit tests

Core tests:

- agent name validation;
- tool name validation;
- valid agent passes;
- missing instructions fails;
- duplicate tools fail;
- invalid JSON schema fails;
- unsupported function shapes fail;
- system duplicate agent names fail;
- empty fanout/sequence fail;
- invalid debate fails.

### 18.2 Adapter tests

MAF adapter tests:

- function tool converts to `AITool`;
- raw `AITool` passes through;
- unsupported raw tool fails;
- `AgentSpec -> ChatClientAgent` materializes with fake `IChatClient`;
- adapter does not call real LLM provider;
- adapter does not expose or assert MAF internals such as inaccessible `ChatOptions` property.

### 18.3 Program tests

Program tests:

- `motif { return x }` builds pure program;
- `run agent input` builds `RunAgent` node;
- `fanout [...]` builds fanout node;
- `runDebate debate input` builds debate node;
- typed `run` rejects incompatible input/output where feasible;
- deterministic testing interpreter can replay fixture outputs.

### 18.4 Golden examples

Maintain examples as proof gates:

- simple single-agent tool example;
- typed output example;
- raw MAF escape hatch example;
- TradingAgents-style system example;
- raw MAF comparison example.

---

## 19. Proof Gates

Do not treat Motif as v1 until these pass:

1. Motif examples are at least 30% shorter than equivalent raw MAF in 2 of 3 core examples.
2. TradingAgents-style system can be expressed in one readable F# file.
3. Validation catches at least five useful mistakes before materialization.
4. `Motif.AgentFramework` remains visibly an adapter, not a runtime.
5. Raw/native escape hatches work.
6. Test interpreter can run a Motif program without LLM/provider calls.
7. Diagram interpreter can render a Motif program graph.
8. API is learnable in roughly 15 minutes by an F#/.NET developer.
9. No hidden execution concepts leak into `Motif.Core`.

---

## 20. Implementation Roadmap

### Phase 0 — already started

- `AgentSpec`
- `ToolRef`
- `OutputSpec`
- validation
- MAF `AgentSpec -> AIAgent`
- fake `IChatClient` tests
- TradingAgents-style sketch

### Phase 1 — system and flow primitives

Add to `Motif.Core`:

- `SystemName`
- `StepName`
- `DebateName`
- `SystemSpec`
- `FlowSpec`
- `FlowStep`
- `DebateSpec`
- `DebateStrategy`
- builder modules: `System`, `Flow`, `Debate`
- validation tests

### Phase 2 — program algebra

Add:

- `ProgramNode`
- `MotifProgram<'output>`
- basic combinators: `run`, `fanout`, `runDebate`, `sequence`, `collect`, `return`;
- optional computation expression `motif { ... }`;
- tests proving AST construction.

### Phase 3 — typed facade

Add:

- `Agent<'input,'output>` wrapper;
- typed `agent<'input,'output>` builder;
- typed `run`;
- typed `Debate<'input,'output>` wrapper;
- compile-time-friendly examples.

### Phase 4 — interpreters

Add early interpreters:

- `Motif.Testing`
- `Motif.Diagrams`
- `Motif.AgentFramework.Workflow` materialization spike

Do MAF workflow materialization only after verifying the current native API through package docs/reflection/tests.

### Phase 5 — backend expansion

Evaluate:

- `Motif.SemanticKernel`
- `Motif.AutoGen`
- `Motif.BotSharp`
- `Motif.Elsa`

Each adapter must have proof examples and capability validation.

---

## 21. Open Questions

1. Should `AgentSpec` require instructions universally, or only warn by default?
2. Should debates require a distinct judge, or can one participant also judge?
3. Should `Fanout` imply parallelism, or only independent branches with interpreter-defined scheduling?
4. How strongly should the typed facade enforce input/output compatibility?
5. Should `Collect` be explicit in user syntax, or implicit after `fanout`?
6. What is the minimal MAF workflow materialization target type for current MAF versions?
7. How much of `ChatClientAgentOptions` should be passed through in `Motif.AgentFramework`?
8. Should diagrams/docs be separate packages or part of developer tooling?
9. Should `Memory`, `Checkpoint`, `Retry`, and `HumanApproval` be described in Core later, or remain purely backend options?

---

## 22. Acceptance Criteria for the Next Implementation Slice

The next slice should be considered complete when:

- `SystemSpec`, `FlowSpec`, `FlowStep`, and `DebateSpec` exist in `Motif.Core`;
- builder modules allow concise construction without computation expressions;
- validation covers duplicate agents, empty fanout/sequence, and invalid debates;
- a TradingAgents-style example uses the new primitives;
- all tests pass with `/opt/data/dotnet/dotnet test Motif.sln --nologo`;
- no MAF dependency is added to `Motif.Core`;
- existing `Motif.AgentFramework` tests still pass.

---

## 23. Short Architectural Summary

Motif has two distinct layers:

```text
Agent authoring:
  agent "market-analyst" { ... } -> AgentSpec

Program authoring:
  motif { let! x = run marketAnalyst input ... } -> MotifProgram<'T>
```

Agents are not monads. Running an agent is an operation in a free-monad-like Motif program.

Interpreters consume Motif programs:

```text
MotifProgram<'T> -> MAF WorkflowBuilder/native workflow
MotifProgram<'T> -> Semantic Kernel orchestration
MotifProgram<'T> -> AutoGen group chat
MotifProgram<'T> -> deterministic test trace
MotifProgram<'T> -> Mermaid diagram
MotifProgram<'T> -> Markdown docs
```

This preserves the core boundary:

> Motif describes. Interpreters materialize. Backends execute.
