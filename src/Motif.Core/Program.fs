namespace Motif

open System

/// One agent-run operation inside a Motif program. This is a description, not execution.
type AgentRunStep =
    { Agent: AgentSpec
      Input: obj
      InputType: Type
      OutputType: Type }

/// Parallel branch operation. Interpreters choose how to materialize concurrency.
type FanoutStep =
    { Branches: ProgramNode list
      ElementOutputType: Type
      OutputType: Type }

/// Ordered operation. This first slice is sequencing, not typed data binding.
and SequenceStep =
    { First: ProgramNode
      Next: ProgramNode
      OutputType: Type }

/// Debate operation. Participants are evaluated for fixture coverage; the judge produces the typed result.
and DebateStep =
    { Participants: ProgramNode list
      Judge: ProgramNode
      Rounds: int
      ParticipantOutputType: Type
      OutputType: Type }

/// Initial/free-like representation of a Motif program.
and ProgramNode =
    | RunAgent of AgentRunStep
    | Fanout of FanoutStep
    | Sequence of SequenceStep
    | Debate of DebateStep

/// A typed facade over the untyped initial representation.
type MotifProgram<'output> =
    { Root: ProgramNode
      OutputType: Type }

module Program =
    let run<'input, 'output> (agent: AgentSpec) (input: 'input) : MotifProgram<'output> =
        { Root =
            RunAgent
                { Agent = agent
                  Input = box input
                  InputType = typeof<'input>
                  OutputType = typeof<'output> }
          OutputType = typeof<'output> }

    let fanout<'output> (branches: MotifProgram<'output> list) : MotifProgram<'output list> =
        { Root =
            Fanout
                { Branches = branches |> List.map _.Root
                  ElementOutputType = typeof<'output>
                  OutputType = typeof<'output list> }
          OutputType = typeof<'output list> }

    let sequence<'ignored, 'output> (first: MotifProgram<'ignored>) (next: MotifProgram<'output>) : MotifProgram<'output> =
        { Root =
            Sequence
                { First = first.Root
                  Next = next.Root
                  OutputType = typeof<'output> }
          OutputType = typeof<'output> }

    let debate<'participantOutput, 'output>
        (rounds: int)
        (participants: MotifProgram<'participantOutput> list)
        (judge: MotifProgram<'output>)
        : MotifProgram<'output> =
        { Root =
            Debate
                { Participants = participants |> List.map _.Root
                  Judge = judge.Root
                  Rounds = rounds
                  ParticipantOutputType = typeof<'participantOutput>
                  OutputType = typeof<'output> }
          OutputType = typeof<'output> }
