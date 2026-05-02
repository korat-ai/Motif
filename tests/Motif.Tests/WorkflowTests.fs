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
    let ``Step prompt combinator returns agent with injected step instructions`` () =
        let prompted =
            spec "market"
            |> Step.prompt "For this step, only analyze price action."

        Assert.Equal(
            Some "You are market.\n\nStep prompt:\nFor this step, only analyze price action.",
            prompted.Instructions)

    [<Fact>]
    let ``Maf sequence materializes workflow with prompted agents`` () =
        [ spec "market" |> Step.prompt "For this step, only analyze price action."
          spec "trader" |> Step.prompt "For this step, produce Buy/Sell/Hold." ]
        |> Maf.sequence "prompted-sequence" client
        |> assertWorkflow "prompted-sequence" 2

    [<Fact>]
    let ``Debate combinator creates debate spec from prompted agents`` () =
        let debate =
            Debate.create "research-debate" 2
                (spec "attacker" |> Step.prompt "Attack the thesis.")
                (spec "defender" |> Step.prompt "Defend the thesis.")
                (spec "judge" |> Step.prompt "Judge at the end.")

        Assert.Equal("research-debate", debate.Name)
        Assert.Equal(2, debate.Rounds)
        Assert.Equal(Some "You are attacker.\n\nStep prompt:\nAttack the thesis.", debate.Attacker.Instructions)
        Assert.Equal(Some "You are defender.\n\nStep prompt:\nDefend the thesis.", debate.Defender.Instructions)
        Assert.Equal(Some "You are judge.\n\nStep prompt:\nJudge at the end.", debate.Judge.Instructions)

    [<Fact>]
    let ``Maf debate accepts settings record and materializes attacker defender rounds then judge workflow`` () =
        let debate =
            Debate.create "research-debate" 2
                (spec "attacker" |> Step.prompt "Attack the thesis for this turn.")
                (spec "defender" |> Step.prompt "Defend the thesis for this turn.")
                (spec "judge" |> Step.prompt "Judge only after all rounds are complete.")

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
