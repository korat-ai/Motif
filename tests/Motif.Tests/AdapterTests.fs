namespace Motif.AgentFramework.Tests

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Microsoft.Agents.AI
open Microsoft.Extensions.AI
open Motif
open Motif.AgentFramework
open Xunit

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

module AdapterTests =
    [<Fact>]
    let ``Maf agent facade materializes a native ChatClientAgent`` () =
        let spec =
            agent "trader" {
                instructions "Make a concise trading decision."
                metadata "description" "Trading assistant"
            }

        match spec |> Maf.agent (new FakeChatClient() :> IChatClient) with
        | Ok agent ->
            let chatAgent = Assert.IsType<ChatClientAgent>(agent)
            Assert.Equal("trader", chatAgent.Name)
            Assert.Equal("Trading assistant", chatAgent.Description)
            Assert.Equal("Make a concise trading decision.", chatAgent.Instructions)
        | Error errors ->
            failwithf "Expected Maf.agent to materialize a ChatClientAgent, got %A" errors

    [<Fact>]
    let ``Maf agent facade accepts multiple function tools`` () =
        let quote =
            Tool.ofSyncFunc "quote" "Lookup quote" (fun (ticker: string) -> $"quote:{ticker}")
            |> Result.defaultWith failwith

        let news =
            Tool.ofFunc "news" "Lookup news" (fun (ticker: string) -> Task.FromResult($"news:{ticker}"))
            |> Result.defaultWith failwith

        let spec =
            agent "market" {
                instructions "Use tools before answering."
                tools [ quote; news ]
            }

        match spec |> Maf.agent (new FakeChatClient() :> IChatClient) with
        | Ok agent ->
            let chatAgent = Assert.IsType<ChatClientAgent>(agent)
            Assert.Equal("market", chatAgent.Name)
        | Error errors ->
            failwithf "Expected tool materialization to succeed, got %A" errors

    [<Fact>]
    let ``function tool becomes Microsoft Extensions AI tool`` () =
        let handler (query: string) = Task.FromResult(query.ToUpperInvariant())

        let tool =
            Tool.ofFunc "shout" "Uppercase text" handler
            |> Result.defaultWith failwith

        match Adapter.toolToAiTool tool with
        | Ok aiTool ->
            Assert.Equal("shout", aiTool.Name)
        | Error error ->
            failwithf "Expected tool conversion to succeed, got %A" error

    [<Fact>]
    let ``non AITool raw values are rejected explicitly`` () =
        let tool =
            Tool.raw "native-placeholder" (box "not a Microsoft.Extensions.AI.AITool")
            |> Result.defaultWith failwith

        match Adapter.toolToAiTool tool with
        | Ok _ -> failwith "Expected raw non-AITool conversion to fail."
        | Error(AdapterError.UnsupportedNativeTool(name, actualType)) ->
            Assert.Equal("native-placeholder", name)
            Assert.Equal(typeof<string>, actualType)
        | Error error ->
            failwithf "Unexpected error: %A" error

    [<Fact>]
    let ``toAgent materializes a native ChatClientAgent without running it`` () =
        let tool =
            Tool.ofFunc "shout" "Uppercase text" (fun (query: string) -> Task.FromResult(query.ToUpperInvariant()))
            |> Result.defaultWith failwith

        let spec =
            Agent.unsafeCreate "researcher"
            |> Agent.withInstructions "Answer carefully."
            |> Agent.withMetadata "description" "Research assistant"
            |> Agent.withTool tool

        match Adapter.toAgent (new FakeChatClient() :> IChatClient) spec with
        | Ok agent ->
            let chatAgent = Assert.IsType<ChatClientAgent>(agent)
            Assert.Equal("researcher", chatAgent.Name)
            Assert.Equal("Research assistant", chatAgent.Description)
            Assert.Equal("Answer carefully.", chatAgent.Instructions)
        | Error errors ->
            failwithf "Expected AgentSpec conversion to succeed, got %A" errors
