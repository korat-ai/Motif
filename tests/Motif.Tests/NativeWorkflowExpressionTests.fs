namespace Motif.AgentFramework.Tests

open System
open Xunit
open Microsoft.Agents.AI
open Microsoft.Agents.AI.Workflows
open Microsoft.Extensions.AI
open Motif.AgentFramework.ComputationExpressions

module NativeWorkflowExpressionTests =
    let private client = new FakeChatClient() :> IChatClient

    let private nativeAgent name instructions =
        ChatClientAgent(client, instructions, name, null, ResizeArray<AITool>(), null, null) :> AIAgent

    let private nativeTool _name =
        AIFunctionFactory.Create(Func<string, string>(fun input -> input))

    let private assertWorkflow expectedName expectedExecutors (workflow: Workflow) =
        Assert.Equal(expectedName, workflow.Name)
        let executors = workflow.ReflectExecutors()
        Assert.True(executors.Count >= expectedExecutors, $"Expected at least {expectedExecutors} executors, got {executors.Count}")

    [<Fact>]
    let ``native MAF workflow expression builds sequential workflow`` () =
        let market = nativeAgent "market" "Analyze market."
        let trader = nativeAgent "trader" "Produce trade decision."

        let workflow =
            mafWorkflow "native-sequence" {
                start (Binding.ofAgent market)
                thenRun (Binding.ofAgent trader)
            }

        workflow |> assertWorkflow "native-sequence" 2

    [<Fact>]
    let ``native MAF workflow expression builds panel with sequence branches`` () =
        let inputBinding = Binding.forwarder "panel-input"
        let market = nativeAgent "market" "Analyze market." |> Binding.ofAgent
        let news = nativeAgent "news" "Analyze news." |> Binding.ofAgent
        let fundamentals = nativeAgent "fundamentals" "Analyze fundamentals." |> Binding.ofAgent
        let risk = nativeAgent "risk" "Analyze risk." |> Binding.ofAgent
        let judge = nativeAgent "judge" "Synthesize reports." |> Binding.ofAgent

        let workflow =
            mafWorkflow "native-panel" {
                start inputBinding
                fanout [ market; fundamentals ]
                edge market news
                edge fundamentals risk
                fanin [ news; risk ] judge
            }

        workflow |> assertWorkflow "native-panel" 6

    [<Fact>]
    let ``agent expression returns native ChatClientAgent`` () =
        let agent =
            agent "market-analyst" {
                chatClient client
                instructions "Analyze market structure."
                description "Market research agent"
                tools [ nativeTool "get_market_data" ]
            }

        let chatAgent = Assert.IsType<ChatClientAgent>(agent)
        Assert.Equal("market-analyst", chatAgent.Name)
        Assert.Equal("Analyze market structure.", chatAgent.Instructions)
        Assert.Equal("Market research agent", chatAgent.Description)

    [<Fact>]
    let ``workflow expression wires trading network from native agents`` () =
        let marketAnalyst = nativeAgent "market-analyst" "Analyze market structure."
        let newsAnalyst = nativeAgent "news-analyst" "Analyze news and sentiment."
        let technicalStrategist = nativeAgent "technical-strategist" "Create trade setup."
        let riskManager = nativeAgent "risk-manager" "Approve or reject trade risk."
        let decisionMaker = nativeAgent "trade-decision-maker" "Make final trade decision."
        let executionAgent = nativeAgent "execution-agent" "Execute approved paper trade."
        let journalAgent = nativeAgent "journal-agent" "Write trading journal."

        let tradingNetwork =
            workflow "trading-network" {
                input "trading-request"
                inParallel "research" [ marketAnalyst; newsAnalyst ]
                thenRun technicalStrategist
                thenRun riskManager
                thenRun decisionMaker
                thenRun executionAgent
                thenRun journalAgent
                output journalAgent
            }

        tradingNetwork |> assertWorkflow "trading-network" 8
