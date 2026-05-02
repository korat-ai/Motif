namespace Motif.AgentFramework

open Microsoft.Agents.AI
open Microsoft.Extensions.AI
open Motif

/// Small F#-friendly facade for materializing Motif specs into native Microsoft Agent Framework objects.
module Maf =
    /// Validate and materialize an AgentSpec into a native MAF AIAgent.
    /// This is only conversion sugar over Adapter.toAgent; execution remains native MAF.
    let agent (client: IChatClient) (spec: AgentSpec) : Result<AIAgent, AdapterError list> =
        Adapter.toAgent client spec
