namespace Motif.Tests

open Xunit
open Motif

module TestHelpers =
    let unwrapOk = function
        | Ok value -> value
        | Error error -> failwithf "Expected Ok, got Error: %A" error

module ValidationTests =
    [<Fact>]
    let ``missing instructions fails validation`` () =
        let spec = Agent.unsafeCreate "agent"

        let result = Validation.validate spec

        Assert.Equal(Error [ MissingInstructions "agent" ], result)

    [<Fact>]
    let ``duplicate tool names fail validation`` () =
        let lookup (_: string) = "ok"

        let toolA = Tool.ofSyncFunc "lookup" "Lookup A" lookup |> TestHelpers.unwrapOk
        let toolB = Tool.ofSyncFunc "lookup" "Lookup B" lookup |> TestHelpers.unwrapOk

        let spec =
            Agent.unsafeCreate "agent"
            |> Agent.withInstructions "Use tools."
            |> Agent.withTool toolA
            |> Agent.withTool toolB

        let result = Validation.validate spec

        Assert.Equal(Error [ DuplicateToolName "lookup" ], result)

    [<Fact>]
    let ``empty json schema output fails validation`` () =
        let spec =
            Agent.unsafeCreate "agent"
            |> Agent.withInstructions "Return structured output."
            |> Agent.withOutput (Output.jsonSchema "" "")

        let result = Validation.validate spec

        Assert.Equal(
            Error [ InvalidOutputSpec "JsonSchema output requires non-empty name and schema." ],
            result)

    [<Fact>]
    let ``valid minimal agent passes validation`` () =
        let spec =
            Agent.unsafeCreate "agent"
            |> Agent.withInstructions "Answer carefully."

        let result = Validation.validate spec

        Assert.Equal(Ok spec, result)
