# Pipeline Alerts for Trading Signals

This repo checks a natural gas pipeline's EBB (Electronic Bulletin Board) notices for potential trading signals and sends alerts to Slack.

The solution is 3 separate parts. 
1) A notice source that downloads the Critical and Planned Service Outage lists for the ANR pipeline, and parses them into a list of Notices
2) A signal evaluator, which analyzes a notice, looking for potential trading signals, or things that could affect the cash price of natural gas at Henry Hub in Louisiana.
3) A notifier, which is either a Slack message, or a printed out mock of a Slack message to the console.

## What constitutes a trading signal?
A signal is deemed a trading signal if it passes all three of these cases:
1) Contains a keyword: Force Majeure, Outage, Curtailment (title or body),
   or "Capacity Reduction" in the title.
   "Non-Force Majeure" is ignored: it's standard tariff wording, not an event.

   The brief's keywords mostly match boilerplate in ANR's notices (every "force majeure" in the saved data is inside "Non-Force Majeure"), so I added "Capacity Reduction", which is how ANR words an actual cut.
2) Mention of __Louisiana__ or known Henry Hub-linked pipelines (Columbia Gulf, Gulf South, NGPL, Transco, etc. The full list is in SignalEvaluator.cs, taken from Wikipedia's Henry Hub article).
3) Notice posted within the last 3 days

While you could interpret a trading signal as fitting any of these (OR logic instead of AND), I believe this would result in a significant amount of noise, and would alert traders excessively for non-relevant notices. On the saved ANR data, the keyword rule alone matches 21 of 57 notices, mostly outside Louisiana.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Internet access: the app reads ANR's live notice board (each run takes ~20 seconds). Tests run offline.
- Optional: a Slack incoming webhook (see below).

Check it's installed:

    dotnet --version    # should print 10.x

Connect a Slack workspace. To get a webhook URL:
1) Create an app at https://api.slack.com/apps and pick your workspace
2) Open Incoming Webhooks and turn it on
3) Click "Add New Webhook to Workspace" and choose a channel
4) Copy the URL (it starts with https://hooks.slack.com/services/)

Slack's guide: https://docs.slack.dev/messaging/sending-messages-using-incoming-webhooks/

Then set it in the SLACK_WEBHOOK_URL environment variable:

    export SLACK_WEBHOOK_URL='https://hooks.slack.com/services/XXX/YYY/ZZZ'

Or on Windows (PowerShell):

    $env:SLACK_WEBHOOK_URL = 'https://hooks.slack.com/services/XXX/YYY/ZZZ'

If Slack webhook isn't set, the app runs in dry-run mode and prints the Slack message instead of sending it.

## To run this project
From the repo root, run in terminal `dotnet run --project src/PipelineAlerts`

To run tests, use `dotnet test`

## Architecture Description
    INoticeSource -> Notice -> ISignalEvaluator -> SignalResult -> INotifier
- `AnrNoticeSource` (an `INoticeSource`) is the only code that touches HTTP and XML. It downloads both lists and uses `AnrXmlParser` to turn them into notices. Everything after that only works with `Notice`.
- `SignalEvaluator` applies the three defined rules of a trading signal to a Notice, and returns a `SignalResult` with the reasons. 
- `SlackNotifier` sends the signal to Slack. `ConsoleNotifier` prints it instead when no webhook is set. Both use `INotifier`, and `Program.cs` picks the appropriate one.
- `Program.cs` is the entry point to the program, structured to wire all the pieces together and produce the console output.
- `CONTRACT.md` defines the rules and structure of the codebase so that agents can read and understand where things are, what can be changed, and how they are organized.

## Technical Choices
- Using interfaces for INoticeSource, ISignalEvaluator, and INotifier in order to be able to easily swap sources, rules, and messaging channels while maintaining the structure.
- Choosing a data source on the TC eConnects platform for their XML downloads, as well as hosting multiple Henry Hub-linked pipelines on the platform.
- Fetching both the Critical and Planned Service Outage sources, because the Critical list alone could miss some capacity reductions.
- Every signal sent gets 3 reasons, for clear explanations as to why it passed the evaluator.
- Failed fetches fail loudly with an error and exit code 1, so that a failed fetch is never mistaken as 0 notices.

## What could you extend for this project in production
__More pipelines__: For full coverage of trading signals, you could write a new source for all Henry Hub-linked pipelines. Each pipeline gets its own `INoticeSource`, which turns that pipeline's format into the same `Notice`. The evaluator and notifier don't change. Pipelines on the same TC eConnects site (Columbia Gulf, TC Louisiana) use the same report, so they'd only need a different asset number (14 for Columbia Gulf, 3119 for TC Louisiana) in config, not new code.

__Scheduling + storage__: The app can run on a schedule today (cron / Task Scheduler; it exits 1 on failure). Without memory it would re-alert the same notice every run, so the two options are a cron job matching a notice window (daily, lookbackDays = 1 which could result in a missed run and missed alerts) or a small record of sent NoticeIds. I'd use the record, keeping the 3-days lookback as a safety net. In this scenario, you could run every few minutes to be notified of a new notice indicating a trading signal almost immediately.

__Config file__: Settings that are hardcoded today (the pipeline URLs and asset numbers, lookbackDays, the keyword and Henry Hub lists) would move into a config file like `appsettings.json`.

## Known limitations

- __Only the first volume is reported.__ Some notices list more than one cut. Notice 26252033 reduces Lake Arthur by 728 MMcf/d and Dugas by 1,100 MMcf/d, but only the 728 is shown. Getting every cut right means reading the pipeline's wording
- __Relevance is a text match, not geography.__ A notice counts if it mentions Louisiana or a Henry Hub-linked pipeline by name. Notice 26250013 is about an interconnect in Joliet, Illinois, but names NGPL, so it passes the relevance rule.
- __LNG terminals aren't covered.__ The brief mentions LNG export terminals, but the relevance list only has Louisiana, Henry Hub and linked pipelines. Adding terminal names is a small change.
- __Most days there are no signals.__ With a 3-day window, ANR usually has nothing that passes all three rules. The one real signal in the saved data (26252033) was posted 9/23, so a demo needs a longer window (lookbackDays in `Program.cs`).
- __No memory between runs.__ The same signal is sent again every run while it's inside the window, and superseded notices aren't linked.
- __ANR only.__ The other TC eConnects pipelines need config, and other sites need their own source.

## How I used AI

I used AI coding agents to implement parts of this project, with myself as the architect and reviewer.

- __What I owned:__ I wrote `CONTRACT.md`, the models (`Notice`, `SignalResult`) and the interfaces before any agent wrote code. I made every rule decision (AND logic, "Capacity Reduction", ignoring "Non-Force Majeure", the Henry Hub list) and wrote this README.
- __What agents did:__ Each agent got one small task (the XML parser, the ANR source, the per-notice link, the evaluator, the Slack notifier), on its own branch and pull request. Their commits show "Agent" as the author, and their pull requests are titled "[agent]" in the history.
- __The limits:__ Agents could only change the files their task needed. They couldn't edit the contract, models or interfaces, push to main, add packages, or send real Slack messages. Done meant it builds and the tests pass.
- __How I checked:__ I reviewed every PR before merging, and added my own commits where something needed changing, like wiring up `Program.cs` and narrowing "Natural Gas Pipeline" to "Natural Gas Pipeline Co" (Natural Gas Pipeline Company of America), since "natural gas pipeline" is a common phrase that would match unrelated notices.
- __Where it was wrong:__ The AI told me ANR had no per-notice link, so every notice would point to the list page. Thinking that couldn't possibly be true, I went looking, and found the real notice page link. That's now the link in every alert.