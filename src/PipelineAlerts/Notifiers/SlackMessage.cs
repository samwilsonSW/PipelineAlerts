using System.Text;
using PipelineAlerts.Models;

namespace PipelineAlerts.Notifiers;

/// <summary>
/// Builds the Slack message text (mrkdwn) for one trading signal. Shared by the real Slack notifier
/// and the dry-run console notifier, so a dry run prints exactly what would be sent.
/// </summary>
public static class SlackMessage
{
    public static string Build(SignalResult signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        var notice = signal.Notice;

        var text = new StringBuilder();
        text.AppendLine($"*Trading signal: {Escape(notice.Subject)}*");
        text.AppendLine($"Type: {Escape(notice.Type)}");
        text.AppendLine($"Pipeline: {Escape(notice.Pipeline)}");
        text.AppendLine($"Volume: {(signal.Volume is null ? "not stated" : Escape(signal.Volume))}");
        // The link markup <url|label> is ours, so it is not escaped. The URL is used as built by the source.
        text.AppendLine($"Notice {Escape(notice.NoticeId)} <{notice.Link.AbsoluteUri}|View notice>");
        text.Append($"Why: {Escape(string.Join("; ", signal.Reasons))}");
        return text.ToString();
    }

    // Slack treats &, < and > as control characters in message text; notice text must not be read as markup.
    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
