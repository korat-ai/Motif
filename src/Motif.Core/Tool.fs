namespace Motif

open System
open System.Threading.Tasks

module Tool =
    let raw (name: string) (value: obj) : Result<ToolRef, string> =
        ToolName.create name
        |> Result.map (fun toolName ->
            NativeTool { Name = toolName; Value = value })

    /// Minimal v0 function tool helper.
    /// This intentionally stores the handler opaquely; shape validation happens in Validation.validate.
    let ofFunc (name: string) (description: string) (handler: 'input -> Task<'output>) : Result<ToolRef, string> =
        ToolName.create name
        |> Result.map (fun toolName ->
            FunctionTool
                { Name = toolName
                  Description = if String.IsNullOrWhiteSpace description then None else Some description
                  Handler = box (Func<'input, Task<'output>>(handler))
                  InputType = Some typeof<'input>
                  OutputType = Some typeof<'output> })

    let ofSyncFunc (name: string) (description: string) (handler: 'input -> 'output) : Result<ToolRef, string> =
        ToolName.create name
        |> Result.map (fun toolName ->
            FunctionTool
                { Name = toolName
                  Description = if String.IsNullOrWhiteSpace description then None else Some description
                  Handler = box (Func<'input, 'output>(handler))
                  InputType = Some typeof<'input>
                  OutputType = Some typeof<'output> })
