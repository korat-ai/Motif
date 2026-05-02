#r "../../src/Motif.Core/bin/Debug/net10.0/Motif.Core.dll"

open Motif

type Ticker = Ticker of string
type MarketReport = MarketReport of string
type Decision = Buy | Sell | Hold

let agent name =
    Agent.unsafeCreate name
    |> Agent.withInstructions $"Instructions for {name}"

let reports =
    Program.fanout [
        Program.run<Ticker, MarketReport> (agent "market") (Ticker "NVDA")
        Program.run<Ticker, MarketReport> (agent "news") (Ticker "NVDA")
    ]

let hasEnoughReports =
    Predicate.quote <@ fun (reports: MarketReport list) -> reports.Length > 1 @>

let trade =
    Program.run<MarketReport list, Decision>
        (agent "trader")
        [ MarketReport "placeholder" ]

let fallback = Program.value Hold

let program = Program.route reports hasEnoughReports trade fallback

let interpreter =
    TestInterpreter.empty
    |> TestInterpreter.withAgentResult<MarketReport> "market" (MarketReport "technical ok")
    |> TestInterpreter.withAgentResult<MarketReport> "news" (MarketReport "news ok")
    |> TestInterpreter.withAgentResult<Decision> "trader" Buy

match TestInterpreter.run program interpreter with
| Ok decision -> printfn "Route decision: %A" decision
| Error error -> failwithf "Unexpected interpreter error: %A" error
