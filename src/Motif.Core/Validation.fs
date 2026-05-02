namespace Motif

open System

module Validation =
    let private agentName spec =
        spec.Name |> AgentName.value

    let private validateInstructions (spec: AgentSpec) =
        match spec.Instructions with
        | None -> [ MissingInstructions(agentName spec) ]
        | Some text when String.IsNullOrWhiteSpace text -> [ EmptyInstructions(agentName spec) ]
        | Some _ -> []

    let private validateDuplicateTools (spec: AgentSpec) =
        spec.Tools
        |> List.map (ToolRef.name >> ToolName.value)
        |> List.groupBy id
        |> List.choose (fun (name, occurrences) ->
            if List.length occurrences > 1 then Some(DuplicateToolName name) else None)

    let private validateOutput (spec: AgentSpec) =
        match spec.Output with
        | None -> []
        | Some(JsonSchema(name, schema)) when String.IsNullOrWhiteSpace name || String.IsNullOrWhiteSpace schema ->
            [ InvalidOutputSpec "JsonSchema output requires non-empty name and schema." ]
        | Some _ -> []

    let validate (spec: AgentSpec) : Result<AgentSpec, ValidationError list> =
        let errors =
            [ yield! validateInstructions spec
              yield! validateDuplicateTools spec
              yield! validateOutput spec ]

        if List.isEmpty errors then Ok spec else Error errors
