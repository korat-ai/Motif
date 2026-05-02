#r "../../src/Motif.AgentFramework.ComputationExpressions/bin/Debug/net10.0/Motif.AgentFramework.ComputationExpressions.dll"
#r "nuget: Microsoft.Extensions.AI, 10.5.1"
#r "nuget: Microsoft.Agents.AI, 1.3.0"
#r "nuget: Microsoft.Agents.AI.Workflows, 1.3.0"

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Microsoft.Agents.AI
open Microsoft.Agents.AI.Workflows
open Microsoft.Extensions.AI
open Motif.AgentFramework.ComputationExpressions

type FakeChatClient() =
    interface IChatClient with
        member _.GetResponseAsync(_messages: IEnumerable<ChatMessage>, _options: ChatOptions, _cancellationToken: CancellationToken) =
            Task.FromResult(ChatResponse())

        member _.GetStreamingResponseAsync(_messages: IEnumerable<ChatMessage>, _options: ChatOptions, _cancellationToken: CancellationToken) =
            Unchecked.defaultof<IAsyncEnumerable<ChatResponseUpdate>>

        member _.GetService(serviceType: Type, _serviceKey: obj) =
            if serviceType = typeof<IChatClient> then box (new FakeChatClient() :> IChatClient) else null

    interface IDisposable with
        member _.Dispose() = ()

let client = new FakeChatClient() :> IChatClient

let nativeAgent name instructions =
    ChatClientAgent(client, instructions, name, null, ResizeArray<AITool>(), null, null) :> AIAgent

let market = nativeAgent "market" "Analyze market."
let news = nativeAgent "news" "Analyze news."
let fundamentals = nativeAgent "fundamentals" "Analyze fundamentals."
let risk = nativeAgent "risk" "Analyze risk."
let judge = nativeAgent "judge" "Synthesize branch outputs."

let sequential =
    mafWorkflow "native-sequence" {
        start (Binding.ofAgent market)
        thenRun (Binding.ofAgent news)
        thenRun (Binding.ofAgent judge)
    }

let panelWithSequenceBranches =
    let input = Binding.forwarder "panel-input"
    let marketB = Binding.ofAgent market
    let newsB = Binding.ofAgent news
    let fundamentalsB = Binding.ofAgent fundamentals
    let riskB = Binding.ofAgent risk
    let judgeB = Binding.ofAgent judge

    mafWorkflow "native-panel-with-sequence-branches" {
        start input
        fanout [ marketB; fundamentalsB ]
        edge marketB newsB
        edge fundamentalsB riskB
        fanin [ newsB; riskB ] judgeB
    }

printfn "Native CE sequential: %s / executors: %i" sequential.Name (sequential.ReflectExecutors().Count)
printfn "Native CE panel: %s / executors: %i" panelWithSequenceBranches.Name (panelWithSequenceBranches.ReflectExecutors().Count)
