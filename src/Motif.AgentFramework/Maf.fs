namespace Motif.AgentFramework

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Microsoft.Agents.AI
open Microsoft.Agents.AI.Workflows
open Microsoft.Extensions.AI
open Motif

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

    /// Materialize a debate as a native MAF round-robin group chat.
    /// Each round gives every participant one turn, followed by the judge.
    let debate (name: string) (maxRounds: int) (client: IChatClient) (participants: AgentSpec list) (judge: AgentSpec) : Result<Workflow, AdapterError list> =
        let turnsPerRound = participants.Length + 1
        let maxIterations = max 1 maxRounds * turnsPerRound
        participants @ [ judge ]
        |> roundRobinChat name maxIterations client

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
