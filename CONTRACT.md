# CONTRACT.md

Read this file first. This is the single source of truth for agents working in this project. It defines the data structures used in the project and the general outline of how it fits together.

Code should strictly follow the outlines in this file. If the code and this file disagree, this file is correct.

AGENTS: This file is READ ONLY. Do not write to this file, or your pull request will be rejected.

If this file looks wrong, or your task needs something it doesn't cover, stop and tell the author. Don't work around it. If your work is rejected, the author will say what to change; push the fix to the same branch.

## What the app does

Pulls recent notices from the ANR pipeline's bulletin board, decides which ones are trading signals, and sends those to Slack.

## The pieces

    Source  ->  Notices  ->  Evaluator  ->  Results  ->  Notifier

- **Source** fetches and parses. It is the only piece that knows about HTTP or XML.
- **Evaluator** applies the signal rules. No network, no files, no clock. The current time is passed in.
- **Notifier** sends signals to Slack, or prints them in dry-run mode. It doesn't decide what is a signal.

Everything after the Source works only with `Notice`. Adding a pipeline means writing a new Source, nothing else.

## Notice

    Pipeline        string           "ANR Pipeline Company"
    NoticeId        string           "26307008". Unique per pipeline.
    PriorNoticeId   string?          The notice this one replaces, if any.
    Type            string           As posted, e.g. "Planned Service Outage".
    Status          string           As posted, e.g. "Initiate", "Supersede".
    Subject         string
    Body            string           Can be empty.
    Location        string?          Only if it can be pulled from the text. Never guessed.
    PostedUtc       DateTimeOffset
    EffectiveUtc    DateTimeOffset?
    EndUtc          DateTimeOffset?
    IsCritical      bool             Critical: "Y" or "N"
    Link            Uri              Link to actual notice (noticeId)

All times are stored in UTC. ANR posts times in Central time (America/Chicago).

## Interfaces

    INoticeSource     string Name; Task<IReadOnlyList<Notice>> FetchAsync(CancellationToken ct)
    ISignalEvaluator  SignalResult Evaluate(Notice notice, DateTimeOffset nowUtc)
    INotifier         Task SendAsync(SignalResult signal, CancellationToken ct)

    SignalResult
        Notice      Notice
        IsSignal    bool
        Reasons     IReadOnlyList<string>
        Volume      string?

- Reasons always explains the decision, whether the notice matched or not.
- Volume is kept exactly as written ("111 MMcf/d", "5,000 Dth"). No unit conversion.

## Signal rules (v1)

A notice is a signal when all of these are true:

1. Recent: posted in the last 3 days. The number of days is a setting (default 3, must be more than 0).
2. Keyword: "force majeure", "outage" or "curtailment" in the subject or body, or "capacity reduction" in the subject. Not case sensitive.
   - Remove "Non-Force Majeure" before checking for "force majeure". It's tariff wording, not an event.
   - The Type field doesn't count. Every notice on the outage list has Type "Planned Service Outage".
3. Relevant: the subject or body mentions Louisiana, Henry Hub, or a Henry Hub-linked pipeline:
   Acadian, Columbia Gulf, Gulf South, Bridgeline, NGPL / Natural Gas Pipeline, Sea Robin,
   Southern Natural, Texas Gas, Transco / Transcontinental, Trunkline, Jefferson Island, Sabine.
   Source: Wikipedia's Henry Hub article. It's a starting list, not a complete one.

Every rule adds a reason to Reasons, whether it passed or failed, e.g. "skipped: no Louisiana / Henry Hub mention".

Volume is reported if found (a number followed by MMcf/d, Dth or MMBtu). It is not required.

Not in v1:
- Alerting only once per replaced chain. Doing that needs to store what was already sent. Replacements are linked by PriorNoticeId only, never by subject.

## When things go wrong

- Site down or timing out: retry a few times, then fail with a clear message. Never report "0 notices" for a failed fetch.
- Data doesn't look like we expect (missing field, date won't parse): fail loudly. Don't skip rows quietly.
- Zero notices from a good response is fine.
- No SLACK_WEBHOOK_URL set: dry-run. Print the message instead of sending.

## ANR source
    Base URL:                https://ebb.tceconnects.com/infopost/ReportViewer.aspx?/InfoPost/Notices
    Critical:                {Base URL}&assetNbr=3005&CritFlag=1&rs:Format=XML
    Planned Service Outage:  {Base URL}&assetNbr=3005&CritFlag=-1&NoticeType=25&rs:Format=XML
    Notice page:             https://ebb.tceconnects.com/infopost/ReportViewer.aspx?/InfoPost/NoticesSubreport&pNoticeId={NoticeId}&AssetNbr=3005

The site opens a notice by postback, so the notice page URL never shows in the address bar. AssetNbr is required: without it the page shows the wrong pipeline name.

On the site: Informational Postings -> ANR Pipeline Company (ANR) -> Notices.
We will get Critical and Planned Service Outages. You can see the manual download via Save Icon dropdown -> XML.

The two lists overlap. Merge by NoticeId so each notice appears once.

The XML has a default namespace (xmlns="Notices"). Queries must use it.

Each notice is a `Detail` element with one nested `Item`:

    Detail@NoticeId      -> NoticeId
    Detail@NoticeType    -> Type
    Detail@Subject       -> Subject
    Detail@PostedDt      -> PostedUtc     "10/14/2026 8:40:34 AM" 
    Detail@EffBeginDt    -> EffectiveUtc  "10/14/26 5:00:00 AM"  (2-digit year)
    Detail@EndDt         -> EndUtc        "12/02/26 6:00:00 PM"  (2-digit year)
    Item@TSPName         -> Pipeline
    Item@NoticeText      -> Body
    Item@NoticeType      -> Status and PriorNoticeId, packed as lines:
                            "Notice Stat Desc: Supersede" / "Prior Notice: 26305023"

Other pipelines on the same site (Columbia Gulf = 14, TC Louisiana = 3119) use the same report with a different assetNbr.

## Console output

    Checking ANR pipeline...
    Found N notices.
    M trading signal(s) detected.
    Slack message sent.

## Rules for agents

- Only change files relevant to the given task.
- No new packages without asking.
- No secrets in code. The webhook comes from the SLACK_WEBHOOK_URL environment variable.
- Never send a real Slack message in tests.
- Never push to main.
- Done means it builds and the tests pass.
- Don't change Models/ or Interfaces/.
- When running the app, unset the webhook so nothing is sent: env -u SLACK_WEBHOOK_URL dotnet run ...