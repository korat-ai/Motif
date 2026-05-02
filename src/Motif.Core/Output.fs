namespace Motif

open System

module Output =
    let jsonSchema (name: string) (schema: string) : OutputSpec =
        JsonSchema(name, schema)

    let dotNetType<'T> () : OutputSpec =
        DotNetType typeof<'T>

    let raw (value: obj) : OutputSpec =
        RawOutput value
