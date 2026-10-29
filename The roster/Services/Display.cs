using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;

namespace ClientShowcase.Services;

/// <summary>Small formatting helpers shared by the public site and the dashboard.</summary>
public static partial class Display
{
    public static string Rank(int rank) => rank.ToString(rank < 10 ? "00" : "0", CultureInfo.InvariantCulture);

    /// <summary>"https://www.instagram.com/jane.doe/" → "@jane.doe"</summary>
    public static string Handle(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url ?? "";
        var segment = uri.AbsolutePath.Trim('/').Split('/').FirstOrDefault() ?? "";
        return segment.Length == 0 ? uri.Host : "@" + segment.TrimStart('@');
    }

    public static string TelHref(string phone) => "tel:" + TelChars().Replace(phone, "");

    /// <summary>Logo monogram: first letter of the last word ("The Roster" → "R").</summary>
    public static string Monogram(string siteName)
    {
        var word = siteName.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return string.IsNullOrEmpty(word) ? "•" : char.ToUpperInvariant(word[0]).ToString();
    }

    /// <summary>
    /// Renders a headline for the line-by-line reveal animation: every word is wrapped for masking,
    /// and text between *asterisks* is set in italics. Each italic phrase is wrapped in a <c>.hl</c> group
    /// so it can wrap onto its own line as a unit instead of leaving a lone word behind.
    /// </summary>
    public static IHtmlContent AnimatedTitle(string text)
    {
        var sb = new StringBuilder();
        var italic = false;
        var index = 0;
        foreach (var part in text.Split('*'))
        {
            var words = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var group = italic && words.Length > 0;
            if (group) sb.Append("<span class=\"hl\">");
            foreach (var word in words)
            {
                var encoded = HtmlEncoder.Default.Encode(word);
                sb.Append(CultureInfo.InvariantCulture, $"<span class=\"w\" style=\"--i:{index++}\"><span>");
                sb.Append(italic ? $"<em>{encoded}</em>" : encoded);
                sb.Append("</span></span> ");
            }
            if (group)
            {
                sb.Length--;
                sb.Append("</span> ");
            }
            italic = !italic;
        }
        return new HtmlString(sb.ToString().TrimEnd());
    }

    /// <summary>Plain-text version of a headline (asterisks removed) for titles and meta tags.</summary>
    public static string PlainTitle(string text) => text.Replace("*", "");

    [GeneratedRegex(@"[^\d+]")]
    private static partial Regex TelChars();
}

/// <summary>
/// Accepts "@handle", "handle" or a full profile URL and turns it into a canonical https URL
/// on the expected domain, so links on the public site can't point anywhere unexpected.
/// </summary>
public static partial class SocialLinks
{
    public static bool TryNormalize(string? input, string network, out string? url, out string? error)
    {
        url = null;
        error = null;
        if (string.IsNullOrWhiteSpace(input)) return true;
        var value = input.Trim();
        var (domain, prefix, label) = network switch
        {
            "tiktok" => ("tiktok.com", "@", "TikTok"),
            "instagram" => ("instagram.com", "", "Instagram"),
            _ => throw new ArgumentOutOfRangeException(nameof(network)),
        };

        if (!value.Contains('/') && !value.Contains('.') || HandleOnly().IsMatch(value))
        {
            var handle = value.TrimStart('@');
            if (!HandleOnly().IsMatch(handle))
            {
                error = $"Enter a valid {label} username or profile link.";
                return false;
            }
            url = $"https://www.{domain}/{prefix}{handle}";
            return true;
        }

        if (!value.StartsWith("http", StringComparison.OrdinalIgnoreCase)) value = "https://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            !(uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)))
        {
            error = $"The {label} link must be a {domain} address.";
            return false;
        }
        url = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri.ToString();
        return true;
    }

    public static bool IsWebUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    [GeneratedRegex(@"^@?[A-Za-z0-9._]{1,30}$")]
    private static partial Regex HandleOnly();
}
