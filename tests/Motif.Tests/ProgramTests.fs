namespace Motif.Tests

open Xunit
open Motif
open Microsoft.FSharp.Quotations

module ProgramTests =
    type Ticker = Ticker of string
    type MarketReport = MarketReport of string
    type FundamentalsReport = FundamentalsReport of string
    type DebateArgument = DebateArgument of string
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
    let ``debate creates a debate operation with participants judge and rounds`` () =
        let bull = Program.run<Ticker, DebateArgument> (agent "bull") (Ticker "NVDA")
        let bear = Program.run<Ticker, DebateArgument> (agent "bear") (Ticker "NVDA")
        let judge = Program.run<DebateArgument list, Decision> (agent "judge") [ DebateArgument "bullish"; DebateArgument "bearish" ]

        let program = Program.debate 2 [ bull; bear ] judge

        Assert.Equal(typeof<Decision>, program.OutputType)
        match program.Root with
        | Debate step ->
            Assert.Equal(2, step.Rounds)
            Assert.Equal(2, List.length step.Participants)
            Assert.Equal(typeof<DebateArgument>, step.ParticipantOutputType)
            Assert.Equal(typeof<Decision>, step.OutputType)
            match step.Judge with
            | RunAgent judgeRun -> Assert.Equal("judge", judgeRun.Agent.Name |> AgentName.value)
            | other -> failwithf "Expected judge RunAgent node, got %A" other
        | other -> failwithf "Expected Debate node, got %A" other

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
    let ``test interpreter evaluates debate participants before judge and returns judge result`` () =
        let program =
            Program.debate 1 [
                Program.run<Ticker, DebateArgument> (agent "bull") (Ticker "NVDA")
                Program.run<Ticker, DebateArgument> (agent "bear") (Ticker "NVDA")
            ] (Program.run<DebateArgument list, Decision> (agent "judge") [ DebateArgument "bull"; DebateArgument "bear" ])

        let interpreter =
            TestInterpreter.empty
            |> TestInterpreter.withAgentResult<DebateArgument> "bull" (DebateArgument "upside")
            |> TestInterpreter.withAgentResult<DebateArgument> "bear" (DebateArgument "downside")
            |> TestInterpreter.withAgentResult<Decision> "judge" Hold

        match TestInterpreter.run program interpreter with
        | Ok decision -> Assert.Equal(Hold, decision)
        | Error error -> failwithf "Expected interpreter success, got %A" error

    [<Fact>]
    let ``test interpreter reports missing debate participant fixture before judge`` () =
        let program =
            Program.debate 1 [
                Program.run<Ticker, DebateArgument> (agent "bull") (Ticker "NVDA")
                Program.run<Ticker, DebateArgument> (agent "bear") (Ticker "NVDA")
            ] (Program.run<DebateArgument list, Decision> (agent "judge") [])

        let interpreter =
            TestInterpreter.empty
            |> TestInterpreter.withAgentResult<DebateArgument> "bull" (DebateArgument "upside")
            |> TestInterpreter.withAgentResult<Decision> "judge" Buy

        match TestInterpreter.run program interpreter with
        | Error (MissingAgentFixture "bear") -> ()
        | other -> failwithf "Expected missing participant fixture error, got %A" other

    [<Fact>]
    let ``route creates a route operation with quoted predicate and typed branches`` () =
        let source =
            Program.fanout [
                Program.run<Ticker, MarketReport> (agent "market") (Ticker "NVDA")
                Program.run<Ticker, MarketReport> (agent "news") (Ticker "NVDA")
            ]
        let predicate = Predicate.quote <@ fun (reports: MarketReport list) -> reports.Length > 1 @>
        let trade = Program.run<MarketReport list, Decision> (agent "trader") [ MarketReport "placeholder" ]
        let fallback = Program.value Hold

        let program = Program.route source predicate trade fallback

        Assert.Equal(typeof<Decision>, program.OutputType)
        match program.Root with
        | Route step ->
            Assert.Equal(typeof<MarketReport list>, step.SourceType)
            Assert.Equal(typeof<Decision>, step.OutputType)
            match step.Predicate.Expression with
            | :? Expr<MarketReport list -> bool> -> ()
            | other -> failwithf "Expected typed quotation, got %A" other
        | other -> failwithf "Expected Route node, got %A" other

    [<Fact>]
    let ``test interpreter evaluates quoted route predicate and chooses true branch`` () =
        let source =
            Program.fanout [
                Program.run<Ticker, MarketReport> (agent "market") (Ticker "NVDA")
                Program.run<Ticker, MarketReport> (agent "news") (Ticker "NVDA")
            ]
        let predicate = Predicate.quote <@ fun (reports: MarketReport list) -> reports.Length > 1 @>
        let program =
            Program.route
                source
                predicate
                (Program.run<MarketReport list, Decision> (agent "trader") [ MarketReport "placeholder" ])
                (Program.value Hold)
        let interpreter =
            TestInterpreter.empty
            |> TestInterpreter.withAgentResult<MarketReport> "market" (MarketReport "technical ok")
            |> TestInterpreter.withAgentResult<MarketReport> "news" (MarketReport "news ok")
            |> TestInterpreter.withAgentResult<Decision> "trader" Buy

        match TestInterpreter.run program interpreter with
        | Ok decision -> Assert.Equal(Buy, decision)
        | Error error -> failwithf "Expected interpreter success, got %A" error

    [<Fact>]
    let ``test interpreter evaluates quoted route predicate and chooses false branch`` () =
        let source =
            Program.fanout [
                Program.run<Ticker, MarketReport> (agent "market") (Ticker "NVDA")
            ]
        let predicate = Predicate.quote <@ fun (reports: MarketReport list) -> reports.Length > 1 @>
        let program =
            Program.route
                source
                predicate
                (Program.run<MarketReport list, Decision> (agent "trader") [ MarketReport "placeholder" ])
                (Program.value Hold)
        let interpreter =
            TestInterpreter.empty
            |> TestInterpreter.withAgentResult<MarketReport> "market" (MarketReport "technical ok")
            |> TestInterpreter.withAgentResult<Decision> "trader" Buy

        match TestInterpreter.run program interpreter with
        | Ok decision -> Assert.Equal(Hold, decision)
        | Error error -> failwithf "Expected interpreter success, got %A" error

    [<Fact>]
    let ``test interpreter reports missing fixture`` () =
        let program = Program.run<Ticker, MarketReport> (agent "market") (Ticker "NVDA")

        match TestInterpreter.run program TestInterpreter.empty with
        | Error (MissingAgentFixture "market") -> ()
        | other -> failwithf "Expected missing fixture error, got %A" other
