namespace Motif.Tests

open Xunit
open Motif

module ProgramTests =
    type Ticker = Ticker of string
    type MarketReport = MarketReport of string
    type FundamentalsReport = FundamentalsReport of string
    type Decision = Buy | Sell | Hold

    let private agent name =
        Agent.unsafeCreate name
        |> Agent.withInstructions $"Instructions for {name}"

    [<Fact>]
    let ``run creates an agent run operation with typed input and output`` () =
        let analyst = agent "market-analyst"

        let program =
            Program.run<Ticker, MarketReport> analyst (Ticker "NVDA")

        Assert.Equal(typeof<MarketReport>, program.OutputType)
        match program.Root with
        | RunAgent step ->
            Assert.Equal("market-analyst", step.Agent.Name |> AgentName.value)
            Assert.Equal(typeof<Ticker>, step.InputType)
            Assert.Equal(typeof<MarketReport>, step.OutputType)
        | other -> failwithf "Expected RunAgent node, got %A" other

    [<Fact>]
    let ``fanout creates a fanout operation over typed branches`` () =
        let market = Program.run<Ticker, MarketReport> (agent "market") (Ticker "NVDA")
        let news = Program.run<Ticker, MarketReport> (agent "news") (Ticker "NVDA")

        let program = Program.fanout [ market; news ]

        Assert.Equal(typeof<MarketReport list>, program.OutputType)
        match program.Root with
        | Fanout step -> Assert.Equal(2, List.length step.Branches)
        | other -> failwithf "Expected Fanout node, got %A" other

    [<Fact>]
    let ``sequence creates an ordered operation and keeps final output type`` () =
        let market = Program.run<Ticker, MarketReport> (agent "market") (Ticker "NVDA")
        let trader = Program.run<MarketReport, Decision> (agent "trader") (MarketReport "bullish")

        let program = Program.sequence market trader

        Assert.Equal(typeof<Decision>, program.OutputType)
        match program.Root with
        | Sequence step ->
            match step.First, step.Next with
            | RunAgent first, RunAgent next ->
                Assert.Equal(typeof<MarketReport>, first.OutputType)
                Assert.Equal(typeof<Decision>, next.OutputType)
            | other -> failwithf "Expected run-agent sequence, got %A" other
        | other -> failwithf "Expected Sequence node, got %A" other

    [<Fact>]
    let ``test interpreter returns configured agent result`` () =
        let analyst = agent "market-analyst"
        let program = Program.run<Ticker, MarketReport> analyst (Ticker "NVDA")
        let interpreter =
            TestInterpreter.empty
            |> TestInterpreter.withAgentResult<MarketReport> "market-analyst" (MarketReport "uptrend")

        match TestInterpreter.run program interpreter with
        | Ok (MarketReport text) -> Assert.Equal("uptrend", text)
        | Error error -> failwithf "Expected interpreter success, got %A" error

    [<Fact>]
    let ``test interpreter evaluates fanout branches`` () =
        let program =
            Program.fanout [
                Program.run<Ticker, MarketReport> (agent "market") (Ticker "NVDA")
                Program.run<Ticker, MarketReport> (agent "news") (Ticker "NVDA")
            ]

        let interpreter =
            TestInterpreter.empty
            |> TestInterpreter.withAgentResult<MarketReport> "market" (MarketReport "technical ok")
            |> TestInterpreter.withAgentResult<MarketReport> "news" (MarketReport "news ok")

        match TestInterpreter.run program interpreter with
        | Ok reports ->
            let values = reports |> List.map (fun (MarketReport text) -> text)
            Assert.Equal<string list>([ "technical ok"; "news ok" ], values)
        | Error error -> failwithf "Expected interpreter success, got %A" error

    [<Fact>]
    let ``test interpreter reports missing fixture`` () =
        let program = Program.run<Ticker, MarketReport> (agent "market") (Ticker "NVDA")

        match TestInterpreter.run program TestInterpreter.empty with
        | Error (MissingAgentFixture "market") -> ()
        | other -> failwithf "Expected missing fixture error, got %A" other
