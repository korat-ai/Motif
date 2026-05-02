#r "../../src/Motif.Core/bin/Debug/net10.0/Motif.Core.dll"
#r "../../src/Motif.AgentFramework/bin/Debug/net10.0/Motif.AgentFramework.dll"
#r "nuget: Microsoft.Extensions.AI, 10.5.1"
#r "nuget: Microsoft.Agents.AI, 1.3.0"
#r "nuget: Microsoft.Agents.AI.Workflows, 1.3.0"

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.AI
open Motif
open Motif.AgentFramework

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

let market =
    agent "market-analyst" {
        instructions "Analyze market structure and price action."
    }

let news =
    agent "news-analyst" {
        instructions "Analyze news and macro context."
    }

let fundamentals =
    agent "fundamentals-analyst" {
        instructions "Analyze fundamentals and company quality."
    }

let coordinator =
    agent "coordinator" {
        instructions "Route the request to the right specialist."
    }

let risk =
    agent "risk-manager" {
        instructions "Analyze risk and position sizing."
    }

let bull =
    agent "bull-researcher" {
        instructions "Argue the bullish case."
    }

let bear =
    agent "bear-researcher" {
        instructions "Argue the bearish case."
    }

let judge =
    agent "research-manager" {
        instructions "Moderate the debate and summarize the decision."
    }

let client = new FakeChatClient() :> IChatClient

let analystFanout =
    [ market; news ]
    |> Maf.concurrent "analyst-fanout" client
    |> Result.defaultWith (fun errors -> failwithf "%A" errors)

let researchDebate =
    Maf.debate "research-debate" 2 client bull bear judge
    |> Result.defaultWith (fun errors -> failwithf "%A" errors)

let analystPanel =
    Maf.panel "analyst-panel" client [ market; news; fundamentals ] judge
    |> Result.defaultWith (fun errors -> failwithf "%A" errors)

let tradingDesk =
    Maf.handoff "trading-desk" client coordinator [ market; news; risk ]
    |> Result.defaultWith (fun errors -> failwithf "%A" errors)

let researchToTrade =
    [ market; news; judge ]
    |> Maf.sequence "research-to-trade" client
    |> Result.defaultWith (fun errors -> failwithf "%A" errors)

printfn "Concurrent workflow: %s / executors: %i" analystFanout.Name (analystFanout.ReflectExecutors().Count)
printfn "Debate workflow: %s / executors: %i" researchDebate.Name (researchDebate.ReflectExecutors().Count)
printfn "Panel workflow: %s / executors: %i" analystPanel.Name (analystPanel.ReflectExecutors().Count)
printfn "Handoff workflow: %s / executors: %i" tradingDesk.Name (tradingDesk.ReflectExecutors().Count)
printfn "Sequential workflow: %s / executors: %i" researchToTrade.Name (researchToTrade.ReflectExecutors().Count)
