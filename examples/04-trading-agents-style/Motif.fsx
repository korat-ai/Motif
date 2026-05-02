// Motif sketch: TradingAgents-style system assembled from compact F# values.
// This is intentionally an example sketch, not part of the compiled test project yet.
// Goal: show the target authoring feel for systems like https://github.com/TauricResearch/TradingAgents

#r "../../src/Motif.Core/bin/Debug/net10.0/Motif.Core.dll"

open Motif

module TradingAgentsStyle =
    type Ticker = Ticker of string
    type Report = Report of string
    type Debate = Debate of string
    type TradePlan = TradePlan of string
    type Decision = Buy | Sell | Hold

    let tool name description fn =
        Tool.ofSyncFunc name description fn |> Result.defaultWith failwith

    // In real usage these would call market/news/fundamental/social APIs.
    let marketData = tool "market_data" "Fetch prices and technical indicators" (fun (Ticker ticker) -> Report $"market report for {ticker}")
    let fundamentals = tool "fundamentals" "Fetch financial statements and ratios" (fun (Ticker ticker) -> Report $"fundamentals for {ticker}")
    let news = tool "news" "Fetch recent company and macro news" (fun (Ticker ticker) -> Report $"news for {ticker}")
    let sentiment = tool "sentiment" "Fetch social/public sentiment" (fun (Ticker ticker) -> Report $"sentiment for {ticker}")

    let agent name instructions tools =
        tools
        |> List.fold (fun spec t -> Agent.withTool t spec)
            (Agent.unsafeCreate name |> Agent.withInstructions instructions)

    let marketAnalyst =
        agent "market-analyst"
            "Produce a concise technical-analysis report. Do not make final trading decisions."
            [ marketData ]

    let fundamentalsAnalyst =
        agent "fundamentals-analyst"
            "Evaluate financial health, valuation, growth, and red flags. Do not make final trading decisions."
            [ fundamentals ]

    let newsAnalyst =
        agent "news-analyst"
            "Summarize market-moving news and macro context. Do not make final trading decisions."
            [ news ]

    let sentimentAnalyst =
        agent "sentiment-analyst"
            "Summarize social/public sentiment and short-term mood. Do not make final trading decisions."
            [ sentiment ]

    let bullResearcher =
        agent "bull-researcher"
            "Argue the strongest bullish case using analyst reports. Be evidence-based."
            []

    let bearResearcher =
        agent "bear-researcher"
            "Argue the strongest bearish case using analyst reports. Be evidence-based."
            []

    let researchManager =
        agent "research-manager"
            "Judge the bull/bear debate and produce a balanced investment thesis."
            []

    let trader =
        agent "trader"
            "Convert the investment thesis into a concrete trade plan with entry, exit, sizing, and uncertainty."
            []

    let aggressiveRisk =
        agent "aggressive-risk-analyst"
            "Assess the trade from a return-seeking aggressive risk perspective."
            []

    let neutralRisk =
        agent "neutral-risk-analyst"
            "Assess the trade from a balanced risk perspective."
            []

    let conservativeRisk =
        agent "conservative-risk-analyst"
            "Assess the trade from a capital-preservation risk perspective."
            []

    let portfolioManager =
        agent "portfolio-manager"
            "Make the final Buy/Sell/Hold decision. Respect risk limits and explain the decision."
            []
        |> Agent.withOutput (Output.dotNetType<Decision> ())

    let agents =
        [ marketAnalyst
          fundamentalsAnalyst
          newsAnalyst
          sentimentAnalyst
          bullResearcher
          bearResearcher
          researchManager
          trader
          aggressiveRisk
          neutralRisk
          conservativeRisk
          portfolioManager ]

    let validation =
        agents |> List.map Validation.validate

    // Target future shape, if Motif proves value beyond single-agent blueprints:
    //
    // tradingSystem {
    //   input<Ticker>
    //
    //   analysts [ marketAnalyst; fundamentalsAnalyst; newsAnalyst; sentimentAnalyst ]
    //     |> fanout
    //     |> collect "analyst-reports"
    //
    //   debate "research-debate" {
    //     rounds 2
    //     bull bullResearcher
    //     bear bearResearcher
    //     judge researchManager
    //   }
    //
    //   step trader
    //
    //   debate "risk-debate" {
    //     rounds 2
    //     participants [ aggressiveRisk; conservativeRisk; neutralRisk ]
    //     judge portfolioManager
    //   }
    //
    //   output<Decision>
    // }
