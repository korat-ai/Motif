namespace Motif.AgentFramework

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Microsoft.Agents.AI
open Microsoft.Agents.AI.Workflows
open Microsoft.Extensions.AI
open Motif

type DebateSpec =
    { Name: string
      Rounds: int
      Attacker: AgentSpec
      Defender: AgentSpec
      Judge: AgentSpec }

type WorkflowSpec =
    { Name: string
      Node: WorkflowNode }

and WorkflowNode =
    | AgentNode of AgentSpec
    | SequenceNode of WorkflowSpec list
    | ConcurrentNode of WorkflowSpec list
    | PanelNode of experts: WorkflowSpec list * judge: AgentSpec
    | DebateNode of DebateSpec

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Workflow =
    let agent (agent: AgentSpec) : WorkflowSpec =
        { Name = AgentName.value agent.Name
          Node = AgentNode agent }

    let sequence (name: string) (steps: WorkflowSpec list) : WorkflowSpec =
        { Name = name
          Node = SequenceNode steps }

    let concurrent (name: string) (branches: WorkflowSpec list) : WorkflowSpec =
        { Name = name
          Node = ConcurrentNode branches }

    let panel (name: string) (experts: WorkflowSpec list) (judge: AgentSpec) : WorkflowSpec =
        { Name = name
          Node = PanelNode(experts, judge) }

    let debate (spec: DebateSpec) : WorkflowSpec =
        { Name = spec.Name
          Node = DebateNode spec }

type WorkflowExpressionBuilder(name: string) =
    member _.Yield(()) =
        []

    member _.Yield(agent: AgentSpec) =
        [ Workflow.agent agent ]

    member _.Yield(spec: WorkflowSpec) =
        [ spec ]

    member _.Zero() = []

    member _.Delay(f: unit -> WorkflowSpec list) = f()

    member _.Combine(left: WorkflowSpec list, right: WorkflowSpec list) =
        left @ right

    member _.Run(steps: WorkflowSpec list) =
        Workflow.sequence name steps

    [<CustomOperation("agent")>]
    member _.Agent(steps: WorkflowSpec list, agent: AgentSpec) =
        steps @ [ Workflow.agent agent ]

    [<CustomOperation("branch")>]
    member _.Branch(steps: WorkflowSpec list, branch: WorkflowSpec) =
        steps @ [ branch ]

[<AutoOpen>]
module WorkflowDsl =
    let workflow (name: string) =
        WorkflowExpressionBuilder(name)

module Step =
    let private appendPrompt (prompt: string) (instructions: string option) =
        let prefix =
            instructions
            |> Option.defaultValue String.Empty
            |> fun value -> value.TrimEnd()

        if String.IsNullOrWhiteSpace prefix then
            $"Step prompt:\n{prompt}"
        else
            $"{prefix}\n\nStep prompt:\n{prompt}"

    /// Return a local copy of an agent with a prompt injected for one workflow step.
    let prompt (prompt: string) (agent: AgentSpec) : AgentSpec =
        { agent with
            Instructions = Some (appendPrompt prompt agent.Instructions) }

module Debate =
    let create (name: string) (rounds: int) (attacker: AgentSpec) (defender: AgentSpec) (judge: AgentSpec) : DebateSpec =
        { Name = name
          Rounds = rounds
          Attacker = attacker
          Defender = defender
          Judge = judge }

/// Small F#-friendly facade for materializing Motif specs into native Microsoft Agent Framework objects.
module Maf =
    /// Validate and materialize an AgentSpec into a native MAF AIAgent.
    /// This is only conversion sugar over Adapter.toAgent; execution remains native MAF.
    let agent (client: IChatClient) (spec: AgentSpec) : Result<AIAgent, AdapterError list> =
        Adapter.toAgent client spec

    let private agents (client: IChatClient) (specs: AgentSpec list) : Result<AIAgent list, AdapterError list> =
        specs
        |> List.fold
            (fun state spec ->
                match state, agent client spec with
                | Ok agents, Ok materialized -> Ok (materialized :: agents)
                | Ok _, Error errors -> Error errors
                | Error existing, Error errors -> Error (existing @ errors)
                | Error existing, Ok _ -> Error existing)
            (Ok [])
        |> Result.map List.rev

    /// Materialize a native MAF sequential workflow from Motif agents.
    let sequence (name: string) (client: IChatClient) (specs: AgentSpec list) : Result<Workflow, AdapterError list> =
        specs
        |> agents client
        |> Result.map (fun materialized -> AgentWorkflowBuilder.BuildSequential(name, materialized))

    let private flattenChatMessages (branches: IList<List<ChatMessage>>) =
        let merged = List<ChatMessage>()

        for branch in branches do
            for message in branch do
                merged.Add(message)

        merged

    /// Materialize a native MAF concurrent/fanout workflow from Motif agents.
    /// The default reducer merges branch chat messages in branch order.
    let concurrent (name: string) (client: IChatClient) (specs: AgentSpec list) : Result<Workflow, AdapterError list> =
        specs
        |> agents client
        |> Result.map (fun materialized ->
            AgentWorkflowBuilder.BuildConcurrent(
                name,
                materialized,
                Func<IList<List<ChatMessage>>, List<ChatMessage>>(flattenChatMessages)))

    /// Materialize a native MAF round-robin group chat workflow.
    /// Agents share one chat-style workflow and speak until maxIterations is reached.
    let roundRobinChat (name: string) (maxIterations: int) (client: IChatClient) (specs: AgentSpec list) : Result<Workflow, AdapterError list> =
        specs
        |> agents client
        |> Result.map (fun materialized ->
            let managerFactory =
                Func<IReadOnlyList<AIAgent>, GroupChatManager>(fun participants ->
                    let termination =
                        Func<RoundRobinGroupChatManager, IEnumerable<ChatMessage>, CancellationToken, ValueTask<bool>>(
                            fun manager _messages _cancellationToken ->
                                ValueTask<bool>(manager.IterationCount >= maxIterations))

                    let manager = RoundRobinGroupChatManager(participants, termination)
                    manager.MaximumIterationCount <- maxIterations
                    manager :> GroupChatManager)

            AgentWorkflowBuilder
                .CreateGroupChatBuilderWith(managerFactory)
                .AddParticipants(materialized)
                .WithName(name)
                .Build())

    /// Materialize a debate as a native MAF sequential workflow.
    /// The attacker and defender alternate for Rounds, then the judge speaks once at the end.
    let debate (client: IChatClient) (spec: DebateSpec) : Result<Workflow, AdapterError list> =
        let rounds = max 1 spec.Rounds
        let debateSteps =
            [ for _ in 1 .. rounds do
                  yield spec.Attacker
                  yield spec.Defender
              yield spec.Judge ]

        debateSteps
        |> sequence spec.Name client

    let private toBinding (agent: AIAgent) : ExecutorBinding =
        ExecutorBinding.op_Implicit(agent)

    /// Materialize a panel workflow: fan out one input to experts, then fan in their reports to a judge.
    let panel (name: string) (client: IChatClient) (experts: AgentSpec list) (judge: AgentSpec) : Result<Workflow, AdapterError list> =
        match agents client experts, agent client judge with
        | Ok materializedExperts, Ok materializedJudge ->
            let start = ChatForwardingExecutor($"{name}-input", ChatForwardingExecutorOptions())
            let startBinding: ExecutorBinding = ExecutorBinding.op_Implicit(start)
            let expertBindings = materializedExperts |> List.map toBinding
            let judgeBinding = toBinding materializedJudge

            let workflow =
                WorkflowBuilder(startBinding)
                    .AddFanOutEdge(startBinding, expertBindings, "expert fanout")
                    .AddFanInBarrierEdge(expertBindings, judgeBinding, "judge after expert reports")
                    .WithOutputFrom(judgeBinding)
                    .WithName(name)
                    .Build()

            Ok workflow
        | Error errors, Ok _ -> Error errors
        | Ok _, Error errors -> Error errors
        | Error expertErrors, Error judgeErrors -> Error (expertErrors @ judgeErrors)

    type private WorkflowEdge =
        | SimpleEdge of fromBinding: ExecutorBinding * toBinding: ExecutorBinding
        | FanOutEdge of fromBinding: ExecutorBinding * toBindings: ExecutorBinding list * label: string
        | FanInBarrierEdge of fromBindings: ExecutorBinding list * toBinding: ExecutorBinding * label: string

    type private WorkflowFragment =
        { Start: ExecutorBinding
          Ends: ExecutorBinding list
          Edges: WorkflowEdge list }

    let private forwardingBinding (id: string) =
        let executor = ChatForwardingExecutor(id, ChatForwardingExecutorOptions())
        ExecutorBinding.op_Implicit(executor)

    let private collectResults (results: Result<'T, AdapterError list> list) =
        let folder state result =
            match state, result with
            | Ok values, Ok value -> Ok(value :: values)
            | Ok _, Error errors -> Error errors
            | Error existing, Ok _ -> Error existing
            | Error existing, Error errors -> Error(existing @ errors)

        results
        |> List.fold folder (Ok [])
        |> Result.map List.rev

    let rec private materializeWorkflowSpec (client: IChatClient) (path: string) (spec: WorkflowSpec) : Result<WorkflowFragment, AdapterError list> =
        match spec.Node with
        | AgentNode agentSpec ->
            agent client agentSpec
            |> Result.map (fun materialized ->
                let binding = toBinding materialized
                { Start = binding
                  Ends = [ binding ]
                  Edges = [] })

        | SequenceNode [] ->
            Error [ InvalidWorkflowSpec $"Workflow '{spec.Name}' sequence must contain at least one step." ]

        | SequenceNode steps ->
            steps
            |> List.mapi (fun index step -> materializeWorkflowSpec client $"{path}-seq-{index}" step)
            |> collectResults
            |> Result.map (fun fragments ->
                let chainEdges =
                    fragments
                    |> List.pairwise
                    |> List.collect (fun (left, right) ->
                        left.Ends
                        |> List.map (fun ending -> SimpleEdge(ending, right.Start)))

                { Start = fragments.Head.Start
                  Ends = fragments |> List.last |> _.Ends
                  Edges = (fragments |> List.collect _.Edges) @ chainEdges })

        | ConcurrentNode [] ->
            Error [ InvalidWorkflowSpec $"Workflow '{spec.Name}' concurrent block must contain at least one branch." ]

        | ConcurrentNode branches ->
            branches
            |> List.mapi (fun index branch -> materializeWorkflowSpec client $"{path}-concurrent-{index}" branch)
            |> collectResults
            |> Result.map (fun fragments ->
                let start = forwardingBinding $"{path}-input"
                let join = forwardingBinding $"{path}-join"
                let branchStarts = fragments |> List.map _.Start
                let branchEnds = fragments |> List.collect _.Ends

                { Start = start
                  Ends = [ join ]
                  Edges =
                    [ FanOutEdge(start, branchStarts, "workflow fanout")
                      FanInBarrierEdge(branchEnds, join, "workflow fanin") ]
                    @ (fragments |> List.collect _.Edges) })

        | PanelNode(experts, judge) ->
            let expertResults =
                experts
                |> List.mapi (fun index expert -> materializeWorkflowSpec client $"{path}-panel-{index}" expert)
                |> collectResults

            match expertResults, agent client judge with
            | Ok fragments, Ok materializedJudge ->
                if List.isEmpty fragments then
                    Error [ InvalidWorkflowSpec $"Workflow '{spec.Name}' panel must contain at least one expert." ]
                else
                    let start = forwardingBinding $"{path}-input"
                    let judgeBinding = toBinding materializedJudge
                    let expertStarts = fragments |> List.map _.Start
                    let expertEnds = fragments |> List.collect _.Ends

                    Ok
                        { Start = start
                          Ends = [ judgeBinding ]
                          Edges =
                            [ FanOutEdge(start, expertStarts, "expert fanout")
                              FanInBarrierEdge(expertEnds, judgeBinding, "judge after expert reports") ]
                            @ (fragments |> List.collect _.Edges) }
            | Error errors, Ok _ -> Error errors
            | Ok _, Error errors -> Error errors
            | Error expertErrors, Error judgeErrors -> Error(expertErrors @ judgeErrors)

        | DebateNode debateSpec ->
            let rounds = max 1 debateSpec.Rounds
            let steps =
                [ for _ in 1 .. rounds do
                      yield Workflow.agent debateSpec.Attacker
                      yield Workflow.agent debateSpec.Defender
                  yield Workflow.agent debateSpec.Judge ]

            Workflow.sequence debateSpec.Name steps
            |> materializeWorkflowSpec client path

    /// Materialize a composable Motif workflow spec into one native MAF Workflow graph.
    /// Nested Motif workflow specs are flattened into one WorkflowBuilder graph; MAF Workflow-in-Workflow nesting is not used.
    let workflow (client: IChatClient) (spec: WorkflowSpec) : Result<Workflow, AdapterError list> =
        spec
        |> materializeWorkflowSpec client spec.Name
        |> Result.map (fun fragment ->
            let applyEdge (builder: WorkflowBuilder) edge =
                match edge with
                | SimpleEdge(fromBinding, toBinding) ->
                    builder.AddEdge(fromBinding, toBinding)
                | FanOutEdge(fromBinding, toBindings, label) ->
                    builder.AddFanOutEdge(fromBinding, toBindings, label)
                | FanInBarrierEdge(fromBindings, toBinding, label) ->
                    builder.AddFanInBarrierEdge(fromBindings, toBinding, label)

            fragment.Edges
            |> List.fold applyEdge (WorkflowBuilder(fragment.Start))
            |> _.WithOutputFrom(fragment.Ends |> List.toArray)
            |> _.WithName(spec.Name)
            |> _.Build())

    let private setWorkflowName (name: string) (workflow: Workflow) =
        typeof<Workflow>.GetProperty("Name").SetValue(workflow, name)
        workflow

    /// Materialize a native MAF handoff workflow.
    /// The coordinator receives the input and can hand off to one of the specialist agents using MAF handoff tools.
    let handoff (name: string) (client: IChatClient) (coordinator: AgentSpec) (specialists: AgentSpec list) : Result<Workflow, AdapterError list> =
        match agent client coordinator, agents client specialists with
        | Ok materializedCoordinator, Ok materializedSpecialists ->
            let workflow =
                AgentWorkflowBuilder
                    .CreateHandoffBuilderWith(materializedCoordinator)
                    .WithHandoffs(materializedCoordinator, materializedSpecialists)
                    .Build()
                |> setWorkflowName name

            Ok workflow
        | Error errors, Ok _ -> Error errors
        | Ok _, Error errors -> Error errors
        | Error coordinatorErrors, Error specialistErrors -> Error (coordinatorErrors @ specialistErrors)
