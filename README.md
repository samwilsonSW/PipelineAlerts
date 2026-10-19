# Pipeline Alerts for Trading Signals

This repo checks a natural gas pipeline's EBB (Electronic Bulletin Board) notices for potential trading signals and sends alerts to Slack.

The solution is 3 separate parts. 
1) A notice source that downloads the Critical and Planned Service Outage lists for the ANR pipeline, and parses them into a list of Notices
2) A signal evaluator, which analyzes a notice, looking for potential trading signals, or things that could affect the cash price of natural gas at Henry Hub in Louisiana.
3) A notifier, which is either a Slack message, or a printed out mock of a Slack message to the console.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Internet access: the app reads ANR's live notice board (each run takes ~20 seconds). Tests run offline.
- Optional: a Slack incoming webhook (see below).

Check it's installed:

    dotnet --version    # should print 10.x

Optional: connect a Slack workspace by setting an incoming webhook URL in the SLACK_WEBHOOK_URL environment variable
    
    export SLACK_WEBHOOK_URL='https://hooks.slack.com/services/XXX/YYY/ZZZ'

If Slack webhook isn't set, the app runs in dry-run mode and prints the Slack message instead of sending it.

## To run this project
From the repo root, run in terminal `dotnet run --project src/PipelineAlerts`

To run tests, use `dotnet test`

## What constitutes a trading signal?
A signal is deemed a trading signal if it passes all three of these cases:
1) Contains a keyword: Force Majeure, Outage, Curtailment (title or body),
   or "Capacity Reduction" in the title.
   "Non-Force Majeure" is ignored: it's standard tariff wording, not an event.

   The brief's keywords mostly match boilerplate in ANR's notices (every "force majeure" in the saved data is inside "Non-Force Majeure"), so I added "Capacity Reduction", which is how ANR words an actual cut.
2) Mention of __Louisiana__ or known Henry Hub-linked pipelines (Columbia Gulf, Gulf South, NGPL, Transco, etc. The full list is in SignalEvaluator.cs, taken from Wikipedia's Henry Hub article).
3) Notice posted within the last 3 days

While you could interpret a trading signal as fitting any of these (OR logic instead of AND), I believe this would result in a significant amount of noise, and would alert traders excessively for non-relevant notices. On the saved ANR data, the keyword rule alone matches 21 of 57 notices, mostly outside Louisiana.

## What could you extend for this project in production
__More pipelines__: For full coverage of trading signals, you could write a new source for all Henry Hub-linked pipelines. Each pipeline gets its own `INoticeSource`, which turns that pipeline's format into the same `Notice`. The evaluator and notifier don't change. Pipelines on the same TC eConnects site (Columbia Gulf, TC Louisiana) use the same report, so they'd only need a different asset number (14 for Columbia Gulf, 3119 for TC Louisiana) in config, not new code.

__Scheduling + storage__: The app can run on a schedule today (cron / Task Scheduler; it exits 1 on failure). Without memory it would re-alert the same notice every run, so the two options are a cron job matching a notice window (daily, lookbackDays = 1 which could result in a missed run and missed alerts) or a small record of sent NoticeIds. I'd use the record, keeping the 3-days lookback as a safety net. In this scenario, you could run every few minutes to be notified of a new notice indicating a trading signal almost immediately.



## Known limitations

- __Only the first volume is reported.__ Some notices list more than one cut. Notice 26252033 reduces Lake Arthur by 728 MMcf/d and Dugas by 1,100 MMcf/d, but only the 728 is shown. Getting every cut right means reading the pipeline's wording (such as not counting "leaving 350 MMcf/d")
- __Relevance is a text match, not geography.__ A notice counts if it mentions Louisiana or a Henry Hub-linked pipeline by name. Notice 26250013 is about an interconnect in Joliet, Illinois, but names NGPL, so it passes the relevance rule.
- __LNG terminals aren't covered.__ The brief mentions LNG export terminals, but the relevance list only has Louisiana, Henry Hub and linked pipelines. Adding terminal names is a small change.
- __Most days there are no signals.__ With a 3-day window, ANR usually has nothing that passes all three rules. The one real signal in the saved data (26252033) was posted 9/23, so a demo needs a longer window.
- __No memory between runs.__ The same signal is sent again every run while it's inside the window, and superseded notices aren't linked. (See Scheduling + storage.)
- __ANR only.__ The other TC eConnects pipelines need config, and other sites need their own source.