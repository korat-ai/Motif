namespace Motif.AgentFramework.Tests

open Xunit
open Microsoft.Agents.AI
open Microsoft.Agents.AI.Workflows
open Microsoft.Extensions.AI
open Motif.AgentFramework.ComputationExpressions

module NativeWorkflowExpressionTests =
    let private client = new FakeChatClient() :> IChatClient

    let private nativeAgent name instructions =
        ChatClientAgent(client, instructions, name, null, ResizeArray<AITool>(), null, null) :> AIAgent

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
        let input = Binding.forwarder "panel-input"
        let market = nativeAgent "market" "Analyze market." |> Binding.ofAgent
        let news = nativeAgent "news" "Analyze news." |> Binding.ofAgent
        let fundamentals = nativeAgent "fundamentals" "Analyze fundamentals." |> Binding.ofAgent
        let risk = nativeAgent "risk" "Analyze risk." |> Binding.ofAgent
        let judge = nativeAgent "judge" "Synthesize reports." |> Binding.ofAgent

        let workflow =
            mafWorkflow "native-panel" {
                start input
                fanout [ market; fundamentals ]
                edge market news
                edge fundamentals risk
                fanin [ news; risk ] judge
            }

        workflow |> assertWorkflow "native-panel" 6
