namespace Motif.Tests

open Xunit
open Motif

module DslTests =
    type Decision = Buy | Sell | Hold

    [<Fact>]
    let ``agent computation expression creates a concise agent spec`` () =
        let spec =
            agent "researcher" {
                instructions "Answer carefully."
                output (Output.dotNetType<Decision> ())
                metadata "role" "research"
            }

        Assert.Equal("researcher", spec.Name |> AgentName.value)
        Assert.Equal(Some "Answer carefully.", spec.Instructions)
        Assert.Equal(Some (DotNetType typeof<Decision>), spec.Output)
        Assert.Equal("research", spec.Metadata["role"])

    [<Fact>]
    let ``agent computation expression attaches tools`` () =
        let lookup =
            Tool.ofSyncFunc "lookup" "Lookup by ticker" (fun (ticker: string) -> $"quote:{ticker}")
            |> TestHelpers.unwrapOk

        let spec =
            agent "market" {
                instructions "Use tools."
                tool lookup
            }

        Assert.Single(spec.Tools) |> ignore
        match spec.Tools.Head with
        | FunctionTool tool -> Assert.Equal("lookup", tool.Name |> ToolName.value)
        | other -> failwithf "Expected function tool, got %A" other

    [<Fact>]
    let ``agent computation expression attaches multiple tools at once`` () =
        let quote =
            Tool.ofSyncFunc "quote" "Lookup quote" (fun (ticker: string) -> $"quote:{ticker}")
            |> TestHelpers.unwrapOk

        let news =
            Tool.ofSyncFunc "news" "Lookup news" (fun (ticker: string) -> $"news:{ticker}")
            |> TestHelpers.unwrapOk

        let spec =
            agent "market" {
                instructions "Use tools."
                tools [ quote; news ]
            }

        Assert.Equal(2, spec.Tools.Length)
        let names =
            spec.Tools
            |> List.map (function
                | FunctionTool tool -> tool.Name |> ToolName.value
                | NativeTool tool -> tool.Name |> ToolName.value)

        Assert.Equal<string list>([ "quote"; "news" ], names)
