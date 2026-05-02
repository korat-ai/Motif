#r "../../src/Motif.Core/bin/Debug/net10.0/Motif.Core.dll"

open Motif

type Ticker = Ticker of string
type Decision = Buy | Sell | Hold

let quoteTool =
    Tool.ofSyncFunc "quote" "Return a mock quote for a ticker" (fun (ticker: string) -> $"quote:{ticker}")
    |> function
        | Ok tool -> tool
        | Error error -> failwith error

let trader =
    agent "trader" {
        instructions "You are a concise trading assistant. Return Buy, Sell, or Hold."
        tool quoteTool
        output (Output.dotNetType<Decision> ())
        metadata "style" "fast-maf-authoring"
    }

printfn "Agent: %s" (trader.Name |> AgentName.value)
printfn "Instructions: %s" (trader.Instructions |> Option.defaultValue "")
printfn "Tools: %d" trader.Tools.Length
printfn "Output: %A" trader.Output
