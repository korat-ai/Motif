namespace Motif.Examples

open Motif

module TriageAgent =
    type Triage =
        { Severity: string
          Summary: string
          SuggestedOwner: string }

    let classifyTicket (text: string) =
        if text.Contains("down") then "high" else "normal"

    let findOwner (_area: string) =
        "platform-team"

    let triageSchema =
        """
        {
          "type": "object",
          "properties": {
            "severity": { "type": "string" },
            "summary": { "type": "string" },
            "suggestedOwner": { "type": "string" }
          },
          "required": ["severity", "summary", "suggestedOwner"]
        }
        """

    let spec =
        Agent.unsafeCreate "support-triage"
        |> Agent.withInstructions "Triage support tickets and return severity, summary, and owner."
        |> Agent.withTool (Tool.ofSyncFunc "classify_ticket" "Classify ticket severity" classifyTicket |> Result.defaultWith failwith)
        |> Agent.withTool (Tool.ofSyncFunc "find_owner" "Find owning team" findOwner |> Result.defaultWith failwith)
        |> Agent.withOutput (Output.jsonSchema "Triage" triageSchema)

    let validation =
        Validation.validate spec
