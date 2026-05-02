namespace Motif.Examples

open Motif

module RawEscapeHatch =
    let summarize (text: string) =
        text.Substring(0, min text.Length 120)

    // Placeholder for a native Microsoft Agent Framework tool object.
    // Motif must pass this through or fail honestly in Motif.Maf.toAgent.
    let existingMafTool : obj =
        box "native-maf-tool-placeholder"

    let spec =
        Agent.unsafeCreate "hybrid-agent"
        |> Agent.withInstructions "Use both Motif typed tools and native MAF tools."
        |> Agent.withTool (Tool.ofSyncFunc "summarize" "Summarize text" summarize |> Result.defaultWith failwith)
        |> Agent.withTool (Tool.raw "native_tool" existingMafTool |> Result.defaultWith failwith)

    let validation =
        Validation.validate spec
