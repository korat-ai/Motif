namespace Motif

open System
/// Deterministic interpreter for MotifProgram values.
/// It does not call models, tools, providers, or MAF. It only consumes configured fixtures.
type TestInterpreterError =
    | MissingAgentFixture of agentName: string
    | FixtureTypeMismatch of agentName: string * expected: Type * actual: Type

type TestInterpreter =
    { AgentResults: Map<string, obj> }

module TestInterpreter =
    let empty =
        { AgentResults = Map.empty }

    let withAgentResult<'output> (agentName: string) (result: 'output) (interpreter: TestInterpreter) =
        { interpreter with AgentResults = interpreter.AgentResults |> Map.add agentName (box result) }

    let private boxTypedFSharpList (elementType: Type) (values: obj list) =
        let listType = typedefof<list<_>>.MakeGenericType(elementType)
        let empty = listType.GetProperty("Empty").GetValue(null)
        let cons = listType.GetMethod("Cons")

        values
        |> List.rev
        |> List.fold (fun tail value -> cons.Invoke(null, [| value; tail |])) empty

    let private evalWith (interpreter: TestInterpreter) =
        let rec evalNode node =
            match node with
            | RunAgent step ->
                let agentName = step.Agent.Name |> AgentName.value

                match interpreter.AgentResults |> Map.tryFind agentName with
                | None -> Error(MissingAgentFixture agentName)
                | Some value ->
                    if isNull value || step.OutputType.IsAssignableFrom(value.GetType()) then
                        Ok value
                    else
                        Error(FixtureTypeMismatch(agentName, step.OutputType, value.GetType()))

            | Fanout step ->
                let rec loop remaining collected =
                    match remaining with
                    | [] -> Ok(boxTypedFSharpList step.ElementOutputType (List.rev collected))
                    | branch :: tail ->
                        match evalNode branch with
                        | Ok value -> loop tail (value :: collected)
                        | Error error -> Error error

                loop step.Branches []

            | Sequence step ->
                match evalNode step.First with
                | Error error -> Error error
                | Ok _ -> evalNode step.Next

        evalNode

    let run<'output> (program: MotifProgram<'output>) (interpreter: TestInterpreter) : Result<'output, TestInterpreterError> =
        match evalWith interpreter program.Root with
        | Error error -> Error error
        | Ok value -> Ok(unbox<'output> value)
