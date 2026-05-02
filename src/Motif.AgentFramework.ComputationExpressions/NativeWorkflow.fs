namespace Motif.AgentFramework.ComputationExpressions

open Microsoft.Agents.AI
open Microsoft.Agents.AI.Workflows

[<RequireQualifiedAccess>]
module Binding =
    /// Convert a native MAF AIAgent into a native MAF ExecutorBinding.
    let ofAgent (agent: AIAgent) : ExecutorBinding =
        ExecutorBinding.op_Implicit(agent)

    /// Create a native MAF chat-forwarding executor binding, useful as graph input/fanout/join node.
    let forwarder (id: string) : ExecutorBinding =
        let executor = ChatForwardingExecutor(id, ChatForwardingExecutorOptions())
        ExecutorBinding.op_Implicit(executor)

type MafWorkflowExpressionBuilder(name: string) =
    member _.Yield(()) : WorkflowBuilder option * ExecutorBinding option =
        None, None

    member _.Zero() : WorkflowBuilder option * ExecutorBinding option =
        None, None

    member _.Delay(f: unit -> WorkflowBuilder option * ExecutorBinding option) =
        f()

    member _.Run(state: WorkflowBuilder option * ExecutorBinding option) : Workflow =
        match state with
        | Some builder, Some output ->
            builder
                .WithOutputFrom(output)
                .WithName(name)
                .Build()
        | Some builder, None ->
            builder
                .WithName(name)
                .Build()
        | None, _ ->
            invalidArg name "MAF workflow expression must start with a binding. Use: start binding"

    [<CustomOperation("start")>]
    member _.Start(_state: WorkflowBuilder option * ExecutorBinding option, binding: ExecutorBinding) =
        Some(WorkflowBuilder(binding)), Some binding

    [<CustomOperation("thenRun")>]
    member _.ThenRun(state: WorkflowBuilder option * ExecutorBinding option, binding: ExecutorBinding) =
        match state with
        | Some builder, Some previous ->
            Some(builder.AddEdge(previous, binding)), Some binding
        | Some builder, None ->
            Some builder, Some binding
        | None, _ ->
            invalidOp "thenRun requires start binding first."

    [<CustomOperation("edge")>]
    member _.Edge(state: WorkflowBuilder option * ExecutorBinding option, fromBinding: ExecutorBinding, toBinding: ExecutorBinding) =
        match state with
        | Some builder, _ ->
            Some(builder.AddEdge(fromBinding, toBinding)), Some toBinding
        | None, _ ->
            invalidOp "edge requires start binding first."

    [<CustomOperation("fanout")>]
    member _.FanOut(state: WorkflowBuilder option * ExecutorBinding option, targets: ExecutorBinding list) =
        match state with
        | Some builder, Some source ->
            Some(builder.AddFanOutEdge(source, targets, "fanout")), None
        | Some _, None ->
            invalidOp "fanout requires a current source binding."
        | None, _ ->
            invalidOp "fanout requires start binding first."

    [<CustomOperation("fanin")>]
    member _.FanIn(state: WorkflowBuilder option * ExecutorBinding option, sources: ExecutorBinding list, target: ExecutorBinding) =
        match state with
        | Some builder, _ ->
            Some(builder.AddFanInBarrierEdge(sources, target, "fanin")), Some target
        | None, _ ->
            invalidOp "fanin requires start binding first."

    [<CustomOperation("output")>]
    member _.Output(state: WorkflowBuilder option * ExecutorBinding option, binding: ExecutorBinding) =
        match state with
        | Some builder, _ -> Some builder, Some binding
        | None, _ -> invalidOp "output requires start binding first."

[<AutoOpen>]
module NativeWorkflowDsl =
    /// Experimental F# computation expression over native Microsoft Agent Framework WorkflowBuilder.
    let mafWorkflow (name: string) =
        MafWorkflowExpressionBuilder(name)
