namespace Motif

open Microsoft.FSharp.Core

/// Lightweight F# authoring DSL for AgentSpec.
type AgentBuilder(name: string) =
    member _.Yield(()) : AgentSpec =
        Agent.unsafeCreate name

    [<CustomOperation("instructions")>]
    member _.Instructions(spec: AgentSpec, text: string) : AgentSpec =
        spec |> Agent.withInstructions text

    [<CustomOperation("tool")>]
    member _.Tool(spec: AgentSpec, tool: ToolRef) : AgentSpec =
        spec |> Agent.withTool tool

    [<CustomOperation("output")>]
    member _.Output(spec: AgentSpec, output: OutputSpec) : AgentSpec =
        spec |> Agent.withOutput output

    [<CustomOperation("jsonOutput")>]
    member _.JsonOutput(spec: AgentSpec, name: string, schema: string) : AgentSpec =
        spec |> Agent.withOutput (Output.jsonSchema name schema)

    [<CustomOperation("metadata")>]
    member _.Metadata(spec: AgentSpec, key: string, value: string) : AgentSpec =
        spec |> Agent.withMetadata key value

[<AutoOpen>]
module Dsl =
    let agent (name: string) = AgentBuilder(name)
