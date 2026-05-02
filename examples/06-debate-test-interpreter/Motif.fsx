#r "../../src/Motif.Core/bin/Debug/net10.0/Motif.Core.dll"

open Motif

type Ticker = Ticker of string
type DebateArgument = DebateArgument of string
type Decision = Buy | Sell | Hold

let agent name =
    Agent.unsafeCreate name
    |> Agent.withInstructions $"Instructions for {name}"

let bull =
    Program.run<Ticker, DebateArgument> (agent "bull-researcher") (Ticker "NVDA")

let bear =
    Program.run<Ticker, DebateArgument> (agent "bear-researcher") (Ticker "NVDA")

let judge =
    Program.run<DebateArgument list, Decision>
        (agent "research-manager")
        [ DebateArgument "bull thesis"; DebateArgument "bear thesis" ]

let program =
    Program.debate 2 [ bull; bear ] judge

let interpreter =
    TestInterpreter.empty
    |> TestInterpreter.withAgentResult<DebateArgument> "bull-researcher" (DebateArgument "Demand remains strong")
    |> TestInterpreter.withAgentResult<DebateArgument> "bear-researcher" (DebateArgument "Valuation is stretched")
    |> TestInterpreter.withAgentResult<Decision> "research-manager" Hold

match TestInterpreter.run program interpreter with
| Ok decision -> printfn "Debate decision: %A" decision
| Error error -> failwithf "Unexpected interpreter error: %A" error
