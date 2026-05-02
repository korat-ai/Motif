namespace Motif.AgentFramework.Tests

open Xunit
open Microsoft.Agents.AI.Workflows
open Microsoft.Extensions.AI
open Motif
open Motif.AgentFramework

module WorkflowTests =
    let private spec name =
        agent name {
            instructions ($"You are {name}.")
        }

    let private client = new FakeChatClient() :> IChatClient

    let private assertWorkflow (expectedName: string) (expectedAgents: int) (result: Result<Workflow, AdapterError list>) =
        match result with
        | Ok workflow ->
            Assert.Equal(expectedName, workflow.Name)
            let executors = workflow.ReflectExecutors()
            Assert.True(executors.Count >= expectedAgents, $"Expected at least {expectedAgents} executors, got {executors.Count}")
        | Error errors -> failwithf "Expected workflow materialization to succeed, got %A" errors

    [<Fact>]
    let ``Maf sequence materializes ordered multi-agent workflow`` () =
        [ spec "market"; spec "trader" ]
        |> Maf.sequence "research-to-trade" client
        |> assertWorkflow "research-to-trade" 2

    [<Fact>]
    let ``Maf concurrent materializes fanout workflow`` () =
        [ spec "market"; spec "news"; spec "fundamentals" ]
        |> Maf.concurrent "analyst-fanout" client
        |> assertWorkflow "analyst-fanout" 3

    [<Fact>]
    let ``Maf roundRobinChat materializes shared chat workflow`` () =
        [ spec "bull"; spec "bear"; spec "judge" ]
        |> Maf.roundRobinChat "research-debate" 6 client
        |> assertWorkflow "research-debate" 3

    [<Fact>]
    let ``AgentStep injects step prompt into agent instructions`` () =
        let step =
            spec "market"
            |> AgentStep.withPrompt "For this step, only analyze price action."
            |> AgentStep.toAgentSpec

        Assert.Equal(
            Some "You are market.\n\nStep prompt:\nFor this step, only analyze price action.",
            step.Instructions)

    [<Fact>]
    let ``Maf sequenceSteps materializes workflow with per-step prompts`` () =
        [ spec "market" |> AgentStep.withPrompt "For this step, only analyze price action."
          spec "trader" |> AgentStep.withPrompt "For this step, produce Buy/Sell/Hold." ]
        |> Maf.sequenceSteps "prompted-sequence" client
        |> assertWorkflow "prompted-sequence" 2

    [<Fact>]
    let ``Maf debate accepts settings record and materializes attacker defender rounds then judge workflow`` () =
        let debate =
            { DebateSpec.Name = "research-debate"
              Rounds = 2
              Attacker = spec "attacker" |> AgentStep.withPrompt "Attack the thesis for this turn."
              Defender = spec "defender" |> AgentStep.withPrompt "Defend the thesis for this turn."
              Judge = spec "judge" |> AgentStep.withPrompt "Judge only after all rounds are complete." }

        Maf.debate client debate
        |> assertWorkflow "research-debate" 5

    [<Fact>]
    let ``Maf panel materializes fanout then judge workflow`` () =
        Maf.panel "analyst-panel" client [ spec "market"; spec "news"; spec "fundamentals" ] (spec "trader")
        |> assertWorkflow "analyst-panel" 4

    [<Fact>]
    let ``Maf handoff materializes coordinator to specialists workflow`` () =
        Maf.handoff "trading-desk" client (spec "coordinator") [ spec "market"; spec "risk"; spec "trader" ]
        |> assertWorkflow "trading-desk" 4
