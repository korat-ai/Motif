namespace Motif

module Agent =
    let create (name: string) : Result<AgentSpec, string> =
        AgentName.create name
        |> Result.map (fun agentName ->
            { Name = agentName
              Instructions = None
              Tools = []
              Output = None
              Metadata = Map.empty })

    let unsafeCreate (name: string) : AgentSpec =
        match create name with
        | Ok spec -> spec
        | Error message -> invalidArg (nameof name) message

    let withInstructions (instructions: string) (spec: AgentSpec) : AgentSpec =
        { spec with Instructions = Some instructions }

    let withTool (tool: ToolRef) (spec: AgentSpec) : AgentSpec =
        { spec with Tools = spec.Tools @ [ tool ] }

    let withTools (tools: ToolRef list) (spec: AgentSpec) : AgentSpec =
        { spec with Tools = spec.Tools @ tools }

    let withOutput (output: OutputSpec) (spec: AgentSpec) : AgentSpec =
        { spec with Output = Some output }

    let withMetadata (key: string) (value: string) (spec: AgentSpec) : AgentSpec =
        { spec with Metadata = spec.Metadata |> Map.add key value }
