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
