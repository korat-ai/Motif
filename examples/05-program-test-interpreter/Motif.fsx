// Motif program + test interpreter example.
// Build first:
//   /opt/data/dotnet/dotnet build Motif.sln --nologo

#r "../../src/Motif.Core/bin/Debug/net10.0/Motif.Core.dll"

open Motif

type Ticker = Ticker of string
type MarketReport = MarketReport of string
type Decision = Buy | Sell | Hold

let agent name instructions =
    Agent.unsafeCreate name
    |> Agent.withInstructions instructions

let marketAnalyst =
    agent "market-analyst" "Produce a concise market report."

let newsAnalyst =
    agent "news-analyst" "Produce a concise news report."

let trader =
    agent "trader" "Convert research context into Buy/Sell/Hold."

let analystFanout : MotifProgram<MarketReport list> =
    Program.fanout [
        Program.run<Ticker, MarketReport> marketAnalyst (Ticker "NVDA")
        Program.run<Ticker, MarketReport> newsAnalyst (Ticker "NVDA")
    ]

let tradeDecision : MotifProgram<Decision> =
    Program.run<MarketReport list, Decision> trader [ MarketReport "placeholder" ]

let program : MotifProgram<Decision> =
    Program.sequence analystFanout tradeDecision

let interpreter =
    TestInterpreter.empty
    |> TestInterpreter.withAgentResult<MarketReport> "market-analyst" (MarketReport "technical trend is positive")
    |> TestInterpreter.withAgentResult<MarketReport> "news-analyst" (MarketReport "news sentiment is neutral")
    |> TestInterpreter.withAgentResult<Decision> "trader" Buy

match TestInterpreter.run program interpreter with
| Ok decision -> printfn "Decision: %A" decision
| Error error -> printfn "Interpreter error: %A" error
