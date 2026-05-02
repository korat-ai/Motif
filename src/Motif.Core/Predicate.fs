namespace Motif

open System
open Microsoft.FSharp.Quotations

/// A quoted predicate preserved as inspectable F# quotation metadata.
type QuotedPredicate =
    { Expression: Expr
      InputType: Type }

/// Typed facade for a quoted predicate over an input value.
type PredicateSpec<'input> =
    { Quoted: QuotedPredicate }

module Predicate =
    let quote<'input> (expression: Expr<'input -> bool>) : PredicateSpec<'input> =
        { Quoted =
            { Expression = expression :> Expr
              InputType = typeof<'input> } }
