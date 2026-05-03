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
let tool (handler: string -> string) : AITool =
    AIFunctionFactory.Create(Func<string, string>(handler))

let getMarketData = tool (fun symbol -> $"market-data:{symbol}")
let getOrderBook = tool (fun symbol -> $"order-book:{symbol}")
let getNews = tool (fun symbol -> $"news:{symbol}")
let getPortfolio = tool (fun account -> $"portfolio:{account}")
let getRiskLimits = tool (fun account -> $"risk-limits:{account}")
let placePaperTrade = tool (fun order -> $"paper-order:{order}")
let writeTradingJournal = tool (fun entry -> $"journal:{entry}")

let marketAnalyst : AIAgent =
    agent "market-analyst" {
        chatClient client
        description "Reads raw market data and order book state."
        instructions """
        Analyze current market structure: trend, volatility, liquidity,
        support/resistance, and abnormal movement. Return compact JSON.
        """
        tools [ getMarketData; getOrderBook ]
    }

let newsAnalyst : AIAgent =
    agent "news-analyst" {
        chatClient client
        description "Reads news and sentiment that can affect the symbol."
        instructions """
        Analyze recent news, macro events, and sentiment. Return only events
        that can affect the trading decision, with confidence.
        """
        tools [ getNews ]
    }

let technicalStrategist : AIAgent =
    agent "technical-strategist" {
        chatClient client
        description "Turns research into a concrete trade setup."
        instructions """
        Use the market and news research to propose long, short, or no_trade.
        Include entry, stop loss, take profit, time horizon, and confidence.
        """
        tools [ getMarketData; getOrderBook ]
    }

let riskManager : AIAgent =
    agent "risk-manager" {
        chatClient client
        description "Hard risk gate before any trade can be executed."
        instructions """
        Validate the proposed setup against portfolio exposure, risk limits,
        volatility, liquidity, and news risk. You may reject the trade.
        """
        tools [ getPortfolio; getRiskLimits ]
    }

let tradeDecisionMaker : AIAgent =
    agent "trade-decision-maker" {
        chatClient client
        description "Final decision maker: buy, sell, or hold."
        instructions """
        Combine research, setup, and risk verdict. Output exactly one decision:
        buy, sell, or hold. Never trade if the risk manager rejects the trade.
        """
    }

let executionAgent : AIAgent =
    agent "execution-agent" {
        chatClient client
        description "Executes approved paper trades only."
        instructions """
        If the decision is hold, do nothing. If buy or sell, place a paper
        trade using the approved parameters.
        """
        tools [ placePaperTrade ]
    }

let journalAgent : AIAgent =
    agent "journal-agent" {
        chatClient client
        description "Writes the final trading journal entry."
        instructions """
        Write a concise markdown trading journal entry with input, signals,
        setup, risk verdict, final decision, execution result, and follow-ups.
        """
        tools [ writeTradingJournal ]
    }

let researchWorkflow : Workflow =
    workflow "research-workflow" {
        input "research-request"
        inParallel "research" [ marketAnalyst; newsAnalyst ]
        thenRun technicalStrategist
        output technicalStrategist
    }

let riskWorkflow : Workflow =
    workflow "risk-review-workflow" {
        start riskManager
        output riskManager
    }

let tradingNetwork : Workflow =
    workflow "paper-trading-network" {
        input "trading-request"
        runWorkflow researchWorkflow
        runWorkflowWithOptions riskWorkflow true false
        thenRun tradeDecisionMaker
        thenRun executionAgent
        thenRun journalAgent
        output journalAgent
    }

printfn "TradingNetwork: %s" tradingNetwork.Name
printfn "Executors: %i" (tradingNetwork.ReflectExecutors().Count)
printfn "Shape: input -> subworkflow research(market, news, technical) -> subworkflow risk(options) -> decision -> execution -> journal"
