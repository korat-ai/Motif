namespace Motif.AgentFramework

open System
open System.Collections.Generic
open Microsoft.Agents.AI
open Microsoft.Extensions.AI
open Motif

/// Errors raised while materializing a Motif blueprint into Microsoft Agent Framework objects.
type AdapterError =
    | ValidationFailed of ValidationError list
    | UnsupportedNativeTool of toolName: string * actualType: Type
    | UnsupportedFunctionTool of toolName: string * reason: string

module Adapter =
    let private toolNameValue (toolName: ToolName) =
        ToolName.value toolName

    /// Convert one Motif tool reference into a Microsoft.Extensions.AI AITool.
    /// Native/raw values must already be AITool instances; Motif does not reinterpret arbitrary objects.
    let toolToAiTool (tool: ToolRef) : Result<AITool, AdapterError> =
        match tool with
        | NativeTool raw ->
            match raw.Value with
            | :? AITool as aiTool -> Ok aiTool
            | null -> Error(UnsupportedNativeTool(toolNameValue raw.Name, typeof<obj>))
            | value -> Error(UnsupportedNativeTool(toolNameValue raw.Name, value.GetType()))

        | FunctionTool spec ->
            match spec.Handler with
            | :? Delegate as handler ->
                let options = AIFunctionFactoryOptions()
                options.Name <- toolNameValue spec.Name
                spec.Description |> Option.iter (fun description -> options.Description <- description)
                AIFunctionFactory.Create(handler, options) :> AITool |> Ok
            | _ ->
                Error(
                    UnsupportedFunctionTool(
                        toolNameValue spec.Name,
                        "Function tools must store a System.Delegate. Use Tool.ofFunc / Tool.ofSyncFunc rather than boxing an arbitrary F# function."))

    let private collectToolResults (tools: ToolRef list) =
        let folder (okTools, errors) tool =
            match toolToAiTool tool with
            | Ok aiTool -> aiTool :: okTools, errors
            | Error error -> okTools, error :: errors

        let okTools, errors = List.fold folder ([], []) tools
        List.rev okTools, List.rev errors

    /// Validate and convert an AgentSpec into a native Microsoft Agent Framework AIAgent.
    /// This is deliberately not a run/stream wrapper: execution remains native MAF.
    let toAgent (client: IChatClient) (spec: AgentSpec) : Result<AIAgent, AdapterError list> =
        match Validation.validate spec with
        | Error validationErrors -> Error [ ValidationFailed validationErrors ]
        | Ok validSpec ->
            let aiTools, toolErrors = collectToolResults validSpec.Tools

            if not (List.isEmpty toolErrors) then
                Error toolErrors
            else
                let name = AgentName.value validSpec.Name
                let instructions = defaultArg validSpec.Instructions ""
                let description = validSpec.Metadata |> Map.tryFind "description" |> Option.toObj
                let tools = ResizeArray<AITool>(aiTools) :> IList<AITool>

                client.AsAIAgent(instructions, name, description, tools, null, null) :> AIAgent |> Ok
