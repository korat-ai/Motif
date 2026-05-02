namespace Motif.Examples

open Motif

module SimpleAgent =
    let search query =
        $"Results for {query}"

    let searchTool =
        Tool.ofSyncFunc "search" "Search documents by query" search

    let spec =
        Agent.unsafeCreate "research-agent"
        |> Agent.withInstructions "Answer using the search tool when needed."
        |> Agent.withTool (searchTool |> Result.defaultWith failwith)

    let validation =
        Validation.validate spec
