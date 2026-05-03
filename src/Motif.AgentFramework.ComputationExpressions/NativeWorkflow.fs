namespace Motif.AgentFramework.ComputationExpressions

open System
open System.Collections.Generic
open Microsoft.Agents.AI
open Microsoft.Agents.AI.Workflows
open Microsoft.Extensions.AI

[<RequireQualifiedAccess>]
module Binding =
    /// Convert a native MAF AIAgent into a native MAF ExecutorBinding.
    let ofAgent (agent: AIAgent) : ExecutorBinding =
        ExecutorBinding.op_Implicit(agent)

    /// Create a native MAF chat-forwarding executor binding, useful as graph input/fanout/join node.
    let forwarder (id: string) : ExecutorBinding =
        let executor = ChatForwardingExecutor(id, ChatForwardingExecutorOptions())
        ExecutorBinding.op_Implicit(executor)

    /// Convert a native MAF Workflow into a native MAF subworkflow executor binding.
    let ofWorkflow (id: string) (workflow: Workflow) : ExecutorBinding =
        SubworkflowBinding(workflow, id, ExecutorOptions.Default) :> ExecutorBinding

    /// Create fresh MAF executor options for a workflow/subworkflow binding.
    let executorOptions autoSendMessageHandlerResultObject autoYieldOutputHandlerResultObject : ExecutorOptions =
        let options = Activator.CreateInstance(typeof<ExecutorOptions>, true) :?> ExecutorOptions
        options.AutoSendMessageHandlerResultObject <- autoSendMessageHandlerResultObject
        options.AutoYieldOutputHandlerResultObject <- autoYieldOutputHandlerResultObject
        options

    /// Convert a native MAF Workflow into a subworkflow binding with explicit executor option flags.
    let ofWorkflowWithOptions (id: string) (workflow: Workflow) autoSendMessageHandlerResultObject autoYieldOutputHandlerResultObject : ExecutorBinding =
        let options = executorOptions autoSendMessageHandlerResultObject autoYieldOutputHandlerResultObject
        SubworkflowBinding(workflow, id, options) :> ExecutorBinding

    /// Convert a native MAF Workflow into a subworkflow binding with native MAF ExecutorOptions.
    let ofWorkflowUsingOptions (id: string) (workflow: Workflow) (options: ExecutorOptions) : ExecutorBinding =
        SubworkflowBinding(workflow, id, options) :> ExecutorBinding

type WorkflowExpressionState =
    { Builder: WorkflowBuilder option
      Current: ExecutorBinding list
      Output: ExecutorBinding option }

[<RequireQualifiedAccess>]
module private WorkflowState =
    let empty : WorkflowExpressionState =
        { Builder = None
          Current = []
          Output = None }

    let requireBuilder operation (state: WorkflowExpressionState) =
        match state.Builder with
        | Some builder -> builder
        | None -> invalidOp ($"{operation} requires input/start first.")

    let requireSingleCurrent operation (state: WorkflowExpressionState) =
        match state.Current with
        | [ current ] -> current
        | [] -> invalidOp ($"{operation} requires a current binding.")
        | _ -> invalidOp ($"{operation} requires a single current binding. Add a thenRun/join step first.")

    let bindAgent (agent: AIAgent) =
        Binding.ofAgent agent

type MafWorkflowExpressionBuilder(name: string) =
    member _.Yield(()) : WorkflowExpressionState =
        WorkflowState.empty

    member _.Zero() : WorkflowExpressionState =
        WorkflowState.empty

    member _.Delay(f: unit -> WorkflowExpressionState) =
        f()

    member _.Run(state: WorkflowExpressionState) : Workflow =
        match state.Builder with
        | Some builder ->
            let builder =
                match state.Output, state.Current with
                | Some output, _ -> builder.WithOutputFrom(output)
                | None, [ current ] -> builder.WithOutputFrom(current)
                | None, _ -> builder

            builder
                .WithName(name)
                .Build()
        | None ->
            invalidArg name "MAF workflow expression must start with a binding. Use: start/input binding"

    [<CustomOperation("start")>]
    member _.Start(_state: WorkflowExpressionState, binding: ExecutorBinding) =
        { Builder = Some(WorkflowBuilder(binding))
          Current = [ binding ]
          Output = Some binding }

    [<CustomOperation("start")>]
    member this.Start(state: WorkflowExpressionState, agent: AIAgent) =
        let binding = WorkflowState.bindAgent agent
        this.Start(state, binding)

    [<CustomOperation("input")>]
    member this.Input(state: WorkflowExpressionState, id: string) =
        let binding = Binding.forwarder id
        this.Start(state, binding)

    [<CustomOperation("thenRun")>]
    member _.ThenRun(state: WorkflowExpressionState, binding: ExecutorBinding) =
        let builder = WorkflowState.requireBuilder "thenRun" state

        match state.Current with
        | [ previous ] ->
            { state with
                Builder = Some(builder.AddEdge(previous, binding))
                Current = [ binding ]
                Output = Some binding }
        | [] ->
            { state with Current = [ binding ]; Output = Some binding }
        | sources ->
            { state with
                Builder = Some(builder.AddFanInBarrierEdge(sources, binding, "thenRun after parallel"))
                Current = [ binding ]
                Output = Some binding }

    [<CustomOperation("thenRun")>]
    member this.ThenRun(state: WorkflowExpressionState, agent: AIAgent) =
        this.ThenRun(state, WorkflowState.bindAgent agent)

    [<CustomOperation("runWorkflow")>]
    member this.RunWorkflow(state: WorkflowExpressionState, id: string, workflow: Workflow) =
        this.ThenRun(state, Binding.ofWorkflow id workflow)

    [<CustomOperation("runWorkflowWithOptions")>]
    member this.RunWorkflowWithOptions(
        state: WorkflowExpressionState,
        id: string,
        workflow: Workflow,
        autoSendMessageHandlerResultObject: bool,
        autoYieldOutputHandlerResultObject: bool) =
        this.ThenRun(
            state,
            Binding.ofWorkflowWithOptions
                id
                workflow
                autoSendMessageHandlerResultObject
                autoYieldOutputHandlerResultObject)

    [<CustomOperation("runWorkflowWithExecutorOptions")>]
    member this.RunWorkflowWithExecutorOptions(state: WorkflowExpressionState, id: string, workflow: Workflow, options: ExecutorOptions) =
        this.ThenRun(state, Binding.ofWorkflowUsingOptions id workflow options)

    [<CustomOperation("edge")>]
    member _.Edge(state: WorkflowExpressionState, fromBinding: ExecutorBinding, toBinding: ExecutorBinding) =
        let builder = WorkflowState.requireBuilder "edge" state
        { state with
            Builder = Some(builder.AddEdge(fromBinding, toBinding))
            Current = [ toBinding ]
            Output = Some toBinding }

    [<CustomOperation("edge")>]
    member this.Edge(state: WorkflowExpressionState, fromAgent: AIAgent, toAgent: AIAgent) =
        this.Edge(state, WorkflowState.bindAgent fromAgent, WorkflowState.bindAgent toAgent)

    [<CustomOperation("fanout")>]
    member _.FanOut(state: WorkflowExpressionState, targets: ExecutorBinding list) =
        let builder = WorkflowState.requireBuilder "fanout" state
        let source = WorkflowState.requireSingleCurrent "fanout" state
        { state with
            Builder = Some(builder.AddFanOutEdge(source, targets, "fanout"))
            Current = targets
            Output = None }

    [<CustomOperation("fanout")>]
    member this.FanOut(state: WorkflowExpressionState, targets: AIAgent list) =
        this.FanOut(state, targets |> List.map WorkflowState.bindAgent)

    [<CustomOperation("inParallel")>]
    member _.InParallel(state: WorkflowExpressionState, label: string, targets: ExecutorBinding list) =
        let builder = WorkflowState.requireBuilder "inParallel" state
        let source = WorkflowState.requireSingleCurrent "inParallel" state
        { state with
            Builder = Some(builder.AddFanOutEdge(source, targets, label))
            Current = targets
            Output = None }

    [<CustomOperation("inParallel")>]
    member this.InParallel(state: WorkflowExpressionState, label: string, targets: AIAgent list) =
        this.InParallel(state, label, targets |> List.map WorkflowState.bindAgent)

    [<CustomOperation("parallel")>]
    member _.Parallel(state: WorkflowExpressionState, label: string, targets: ExecutorBinding list) =
        let builder = WorkflowState.requireBuilder "parallel" state
        let source = WorkflowState.requireSingleCurrent "parallel" state
        { state with
            Builder = Some(builder.AddFanOutEdge(source, targets, label))
            Current = targets
            Output = None }

    [<CustomOperation("parallel")>]
    member this.Parallel(state: WorkflowExpressionState, label: string, targets: AIAgent list) =
        this.Parallel(state, label, targets |> List.map WorkflowState.bindAgent)

    [<CustomOperation("fanin")>]
    member _.FanIn(state: WorkflowExpressionState, sources: ExecutorBinding list, target: ExecutorBinding) =
        let builder = WorkflowState.requireBuilder "fanin" state
        { state with
            Builder = Some(builder.AddFanInBarrierEdge(sources, target, "fanin"))
            Current = [ target ]
            Output = Some target }

    [<CustomOperation("fanin")>]
    member this.FanIn(state: WorkflowExpressionState, sources: AIAgent list, target: AIAgent) =
        this.FanIn(state, sources |> List.map WorkflowState.bindAgent, WorkflowState.bindAgent target)

    [<CustomOperation("output")>]
    member _.Output(state: WorkflowExpressionState, binding: ExecutorBinding) =
        WorkflowState.requireBuilder "output" state |> ignore
        { state with Output = Some binding }

    [<CustomOperation("output")>]
    member this.Output(state: WorkflowExpressionState, agent: AIAgent) =
        this.Output(state, WorkflowState.bindAgent agent)

type AgentState =
    { ChatClient: IChatClient option
      Instructions: string option
      Description: string option
      Tools: AITool list }

[<Sealed>]
type AgentExpressionBuilder(name: string) =
    member _.Yield(()) : AgentState =
        { ChatClient = None
          Instructions = None
          Description = None
          Tools = [] }

    member _.Zero() : AgentState =
        { ChatClient = None
          Instructions = None
          Description = None
          Tools = [] }

    member _.Delay(f: unit -> AgentState) =
        f()

    member _.Run(state: AgentState) : AIAgent =
        let client =
            state.ChatClient
            |> Option.defaultWith (fun () -> invalidOp ($"agent '{name}' requires chatClient client."))

        let instructions = state.Instructions |> Option.defaultValue ""
        let description = state.Description |> Option.toObj
        let tools = ResizeArray<AITool>(state.Tools)
        ChatClientAgent(client, instructions, name, description, tools, null, null) :> AIAgent

    [<CustomOperation("chatClient")>]
    member _.ChatClient(state: AgentState, client: IChatClient) =
        { state with ChatClient = Some client }

    [<CustomOperation("instructions")>]
    member _.Instructions(state: AgentState, text: string) =
        { state with Instructions = Some text }

    [<CustomOperation("description")>]
    member _.Description(state: AgentState, text: string) =
        { state with Description = Some text }

    [<CustomOperation("tool")>]
    member _.Tool(state: AgentState, tool: AITool) =
        { state with Tools = state.Tools @ [ tool ] }

    [<CustomOperation("tools")>]
    member _.Tools(state: AgentState, tools: AITool list) =
        { state with Tools = state.Tools @ tools }

[<AutoOpen>]
module NativeWorkflowDsl =
    /// Experimental F# computation expression over native Microsoft Agent Framework WorkflowBuilder.
    let mafWorkflow (name: string) =
        MafWorkflowExpressionBuilder(name)

    /// F# computation expression that returns a native Microsoft Agent Framework AIAgent.
    let agent (name: string) =
        AgentExpressionBuilder(name)

    /// F# computation expression that returns a native Microsoft Agent Framework Workflow.
    let workflow (name: string) =
        MafWorkflowExpressionBuilder(name)
