namespace Motif

open System

/// Stable identifier for an agent blueprint.
type AgentName = private AgentName of string

module AgentName =
    let create (value: string) =
        if String.IsNullOrWhiteSpace value then
            Error "Agent name cannot be empty."
        else
            Ok (AgentName value)

    let value (AgentName value) = value

/// Stable identifier for a tool exposed to the model.
type ToolName = private ToolName of string

module ToolName =
    let create (value: string) =
        if String.IsNullOrWhiteSpace value then
            Error "Tool name cannot be empty."
        else
            Ok (ToolName value)

    let value (ToolName value) = value

/// A raw MAF/native tool object. Motif does not interpret it.
type RawTool =
    { Name: ToolName
      Value: obj }

/// A Motif-supported function tool. The Adapter field is intentionally opaque
/// until Motif.Maf maps it into a native Microsoft Agent Framework tool.
type FuncToolSpec =
    { Name: ToolName
      Description: string option
      Handler: obj
      InputType: Type option
      OutputType: Type option }

/// Tool references accepted by an AgentSpec.
type ToolRef =
    | FunctionTool of FuncToolSpec
    | NativeTool of RawTool

module ToolRef =
    let name = function
        | FunctionTool spec -> spec.Name
        | NativeTool raw -> raw.Name

/// Structured-output declaration. In v0 this is metadata only;
/// Motif must not become a serializer/runtime validator.
type OutputSpec =
    | JsonSchema of name: string * schema: string
    | DotNetType of Type
    | RawOutput of obj

/// The central Motif primitive: an immutable, pre-runtime blueprint for a MAF agent.
type AgentSpec =
    { Name: AgentName
      Instructions: string option
      Tools: ToolRef list
      Output: OutputSpec option
      Metadata: Map<string, string> }

/// Validation errors that can be detected before materializing a MAF AIAgent.
type ValidationError =
    | MissingInstructions of agentName: string
    | DuplicateToolName of toolName: string
    | EmptyInstructions of agentName: string
    | UnsupportedToolFunctionShape of toolName: string * reason: string
    | InvalidOutputSpec of reason: string
