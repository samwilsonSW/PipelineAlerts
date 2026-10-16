using System.Globalization;
using System.Xml.Linq;
using PipelineAlerts.Models;

namespace PipelineAlerts.Sources.Anr;

/// <summary>Turns ANR's saved XML notice report into Notice records. No network.</summary>
public static class AnrXmlParser
{
    private static readonly XNamespace Ns = "Notices";
    // NAESB time zone standard is to use CCT, which is CST OR CDT, depending on the time of year
    private static readonly TimeZoneInfo Central = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    private const string PostedFormat = "M/d/yyyy h:mm:ss tt";
    private const string ShortFormat = "M/d/yy h:mm:ss tt";

    public static IReadOnlyList<Notice> Parse(string xml, Uri listLink)
    {
        var doc = XDocument.Parse(xml);
        return doc.Descendants(Ns + "Detail").Select(d => ParseDetail(d, listLink)).ToList();
    }

    private static Notice ParseDetail(XElement detail, Uri listLink)
    {
        var id = Required(detail, "NoticeId", null);
        var item = detail.Descendants(Ns + "Item").SingleOrDefault()
            ?? throw new FormatException($"ANR notice {id}: missing Item element.");

        var packed = Required(item, "NoticeType", id);
        var body = ((string?)item.Attribute("NoticeText") ?? "").Trim();
        var prior = PackedValue(packed, "Prior Notice", id);

        return new Notice
        {
            Pipeline = Required(item, "TSPName", id).Trim(),
            NoticeId = id,
            PriorNoticeId = prior == "" ? null : prior,
            Type = Required(detail, "NoticeType", id),
            Status = PackedValue(packed, "Notice Stat Desc", id),
            Subject = Required(detail, "Subject", id).Trim(),
            Body = body,
            Location = FindLocation(body),
            PostedUtc = ToUtc(Required(detail, "PostedDt", id), PostedFormat, id, "PostedDt"),
            EffectiveUtc = OptionalDate(detail, "EffBeginDt", id),
            EndUtc = OptionalDate(detail, "EndDt", id),
            IsCritical = (string?)detail.Attribute("CriticalInd") == "Y",
            Link = listLink,
        };
    }

    private static string Required(XElement element, string name, string? id)
    {
        var value = (string?)element.Attribute(name);
        if (string.IsNullOrWhiteSpace(value))
            throw new FormatException($"ANR notice {id ?? "(unknown id)"}: missing {element.Name.LocalName}@{name}.");
        return value;
    }

    private static DateTimeOffset? OptionalDate(XElement detail, string name, string id)
    {
        var value = (string?)detail.Attribute(name);
        return string.IsNullOrWhiteSpace(value) ? null : ToUtc(value, ShortFormat, id, name);
    }

    private static DateTimeOffset ToUtc(string value, string format, string id, string field)
    {
        if (!DateTime.TryParseExact(value.Trim(), format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            throw new FormatException($"ANR notice {id}: {field} '{value}' is not in format '{format}'.");
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, Central), TimeSpan.Zero);
    }

    // Item@NoticeType packs several "Label: value" lines, e.g. "Notice Stat Desc: Supersede".
    private static string PackedValue(string packed, string label, string id)
    {
        var prefix = label + ":";
        var line = packed.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal))
            ?? throw new FormatException($"ANR notice {id}: Item@NoticeType has no '{label}' line.");
        return line[prefix.Length..].Trim();
    }

    // "... located in Jefferson Parish, Louisiana. ..." -> "Jefferson Parish, Louisiana"
    private static string? FindLocation(string body)
    {
        const string marker = "located in ";
        var start = body.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += marker.Length;
        var end = body.IndexOf('.', start);
        if (end < 0) return null;
        var location = body[start..end].Trim();
        return location == "" ? null : location;
    }
}
