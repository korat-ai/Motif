namespace Motif

open System
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Quotations.Patterns
/// Deterministic interpreter for MotifProgram values.
/// It does not call models, tools, providers, or MAF. It only consumes configured fixtures.
type TestInterpreterError =
    | MissingAgentFixture of agentName: string
    | FixtureTypeMismatch of agentName: string * expected: Type * actual: Type
    | UnsupportedPredicateExpression of expression: string

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

    let private unsupported expr =
        Error(UnsupportedPredicateExpression(sprintf "%A" expr))

    let private compare (left: obj) (right: obj) =
        match left with
        | :? IComparable as comparable -> comparable.CompareTo(right)
        | _ -> invalidOp $"Value of type {left.GetType().FullName} is not comparable."

    let private evalQuotedPredicate (predicate: QuotedPredicate) (input: obj) =
        let rec eval env expr =
            match expr with
            | Lambda(var, body) ->
                eval (env |> Map.add var.Name input) body
            | Var var ->
                match env |> Map.tryFind var.Name with
                | Some value -> Ok value
                | None -> unsupported expr
            | Value(value, _) -> Ok value
            | PropertyGet(Some target, property, []) ->
                match eval env target with
                | Ok value -> Ok(property.GetValue(value, null))
                | Error error -> Error error
            | PropertyGet(None, property, []) -> Ok(property.GetValue(null, null))
            | Call(None, methodInfo, args) ->
                let rec evalArgs remaining collected =
                    match remaining with
                    | [] -> Ok(List.rev collected)
                    | arg :: tail ->
                        match eval env arg with
                        | Ok value -> evalArgs tail (value :: collected)
                        | Error error -> Error error

                match evalArgs args [] with
                | Error error -> Error error
                | Ok [ left; right ] ->
                    match methodInfo.Name with
                    | "op_GreaterThan" -> Ok(box (compare left right > 0))
                    | "op_GreaterThanOrEqual" -> Ok(box (compare left right >= 0))
                    | "op_LessThan" -> Ok(box (compare left right < 0))
                    | "op_LessThanOrEqual" -> Ok(box (compare left right <= 0))
                    | "op_Equality" -> Ok(box (Object.Equals(left, right)))
                    | "op_Inequality" -> Ok(box (not (Object.Equals(left, right))))
                    | "op_BooleanAnd" -> Ok(box ((unbox<bool> left) && (unbox<bool> right)))
                    | "op_BooleanOr" -> Ok(box ((unbox<bool> left) || (unbox<bool> right)))
                    | _ -> unsupported expr
                | Ok _ -> unsupported expr
            | Coerce(inner, _) -> eval env inner
            | _ -> unsupported expr

        match eval Map.empty predicate.Expression with
        | Ok (:? bool as decision) -> Ok decision
        | Ok other -> Error(UnsupportedPredicateExpression $"Predicate returned {other.GetType().FullName}, expected Boolean")
        | Error error -> Error error

    let private evalWith (interpreter: TestInterpreter) =
        let rec evalNode node =
            match node with
            | Return step -> Ok step.Value

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

            | Debate step ->
                let rec evalParticipants remaining =
                    match remaining with
                    | [] -> evalNode step.Judge
                    | participant :: tail ->
                        match evalNode participant with
                        | Ok _ -> evalParticipants tail
                        | Error error -> Error error

                evalParticipants step.Participants

            | Route step ->
                match evalNode step.Source with
                | Error error -> Error error
                | Ok sourceValue ->
                    match evalQuotedPredicate step.Predicate sourceValue with
                    | Error error -> Error error
                    | Ok true -> evalNode step.IfTrue
                    | Ok false -> evalNode step.IfFalse

        evalNode

    let run<'output> (program: MotifProgram<'output>) (interpreter: TestInterpreter) : Result<'output, TestInterpreterError> =
        match evalWith interpreter program.Root with
        | Error error -> Error error
        | Ok value -> Ok(unbox<'output> value)
