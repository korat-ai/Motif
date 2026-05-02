#r "../../src/Motif.Core/bin/Debug/net10.0/Motif.Core.dll"
#r "../../src/Motif.AgentFramework/bin/Debug/net10.0/Motif.AgentFramework.dll"
#r "nuget: Microsoft.Extensions.AI, 10.5.1"
#r "nuget: Microsoft.Agents.AI, 1.3.0"

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Microsoft.Agents.AI
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
            if serviceType = typeof<IChatClient> then
                box (new FakeChatClient() :> IChatClient)
            else
                null

    interface IDisposable with
        member _.Dispose() = ()

type Decision = Buy | Sell | Hold

let quoteTool =
    Tool.ofSyncFunc "quote" "Return a mock quote for a ticker" (fun (ticker: string) -> $"quote:{ticker}:123.45")
    |> Result.defaultWith failwith

let newsTool =
    Tool.ofFunc "news" "Return mock news for a ticker" (fun (ticker: string) -> Task.FromResult($"news:{ticker}:calm"))
    |> Result.defaultWith failwith

let trader =
    agent "trader" {
        instructions "You are a concise trading assistant. Use tools, then return Buy, Sell, or Hold."
        tools [ quoteTool; newsTool ]
        output (Output.dotNetType<Decision> ())
        metadata "description" "Trading assistant"
    }

match trader |> Maf.agent (new FakeChatClient() :> IChatClient) with
| Ok nativeAgent ->
    let chatAgent = nativeAgent :?> ChatClientAgent
    printfn "MAF agent: %s" chatAgent.Name
    printfn "Description: %s" chatAgent.Description
    printfn "Instructions: %s" chatAgent.Instructions
    printfn "Motif tools: %i" trader.Tools.Length
| Error errors ->
    failwithf "Could not materialize MAF agent: %A" errors
