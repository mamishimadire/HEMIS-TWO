using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace HemisAudit.Controllers;

[Authorize]
public class DirectivesController : Controller
{
    private const string BaseUrl = "https://www.heda.co.za/Valpac_Help/";
    private readonly IHttpClientFactory _httpFactory;

    public DirectivesController(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }

    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> Proxy([FromQuery] string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith(BaseUrl, StringComparison.OrdinalIgnoreCase))
            return Json(new { success = false, error = "Invalid URL" });

        try
        {
            var client = _httpFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.5");
            request.Headers.TryAddWithoutValidation("Referer", BaseUrl);

            using var response = await client.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
                return Json(new { success = false, error = $"Site returned {(int)response.StatusCode}" });

            var html = await response.Content.ReadAsStringAsync(cts.Token);

            html = Regex.Replace(html, @"<style[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<link[^>]*rel=""stylesheet""[^>]*/?>", "", RegexOptions.IgnoreCase);

            html = Regex.Replace(html, @"<a\b([^>]*)>", match =>
            {
                var attrs = match.Groups[1].Value;
                attrs = Regex.Replace(attrs, @"href=""([^""]+)""", hrefMatch =>
                {
                    var href = hrefMatch.Groups[1].Value;
                    if (href.StartsWith("#", StringComparison.Ordinal) ||
                        href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                        href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                        href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
                    {
                        return hrefMatch.Value;
                    }

                    var absolute = ResolveAbsoluteUrl(url, href);
                    if (absolute.Contains("heda.co.za/Valpac_Help/", StringComparison.OrdinalIgnoreCase))
                    {
                        var escaped = absolute.Replace("\"", "&quot;");
                        return $"href=\"#\" data-valpac-url=\"{escaped}\"";
                    }

                    return hrefMatch.Value;
                }, RegexOptions.IgnoreCase);

                return $"<a{attrs}>";
            }, RegexOptions.IgnoreCase);

            html = Regex.Replace(html, @"<img\b([^>]*)src=""(?!https?:|//)([^""]+)""", match =>
            {
                var prefix = match.Groups[1].Value;
                var src = match.Groups[2].Value;
                var absolute = ResolveAbsoluteUrl(url, src);
                return $"<img{prefix}src=\"{absolute}\"";
            }, RegexOptions.IgnoreCase);

            var bodyMatch = Regex.Match(html, @"<body[^>]*>([\s\S]*?)</body>", RegexOptions.IgnoreCase);
            var body = bodyMatch.Success ? bodyMatch.Groups[1].Value : html;
            body = body.Replace("&nbsp;", " ").Replace("\uFFFD", "");

            var titleMatch = Regex.Match(html, @"<title[^>]*>([^<]*)</title>", RegexOptions.IgnoreCase);
            var title = titleMatch.Success ? titleMatch.Groups[1].Value.Trim() : "Valpac Help";

            return Json(new { success = true, title, body, url });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpGet]
    public IActionResult Sitemap()
    {
        var pages = new[]
        {
            new { id = "main", label = "Main Menu", url = BaseUrl + "Main_menu.htm", group = "Home", sub = false },
            new { id = "intro", label = "(A) Introduction", url = BaseUrl + "Intro.htm", group = "Home", sub = false },
            new { id = "history", label = "(B) History of Amendments", url = BaseUrl + "History.htm", group = "Home", sub = false },
            new { id = "contacts", label = "(C) Contacts", url = BaseUrl + "Contacts.htm", group = "Home", sub = false },
            new { id = "steps", label = "(D) Steps in Preparing Returns", url = BaseUrl + "Steps.htm", group = "Preparation", sub = false },
            new { id = "scopes", label = "(E) File Scopes & Dates", url = BaseUrl + "Scopes.htm", group = "Preparation", sub = false },
            new { id = "files", label = "(F) File Structures", url = BaseUrl + "Files.htm", group = "Preparation", sub = false },
            new { id = "ded_base", label = "(G) Base Element Dictionary", url = BaseUrl + "DEDBase.htm", group = "Data Elements", sub = false },
            new { id = "e001", label = "> 001-010 Qualification & Student", url = BaseUrl + "ded_001_010.htm", group = "Data Elements", sub = true },
            new { id = "e011", label = "> 011-020 Student Demographics", url = BaseUrl + "DED_011_020.htm", group = "Data Elements", sub = true },
            new { id = "e021", label = "> 021-030 Student Status", url = BaseUrl + "DED_021_030.htm", group = "Data Elements", sub = true },
            new { id = "e031", label = "> 031-040 Course & Staff", url = BaseUrl + "DED_031_040.htm", group = "Data Elements", sub = true },
            new { id = "e041", label = "> 041-050 Staff Employment", url = BaseUrl + "DED_041_050.htm", group = "Data Elements", sub = true },
            new { id = "e051", label = "> 051-060 Student Info", url = BaseUrl + "DED_051_060.htm", group = "Data Elements", sub = true },
            new { id = "e061", label = "> 061-070 Institution & Course", url = BaseUrl + "DED_061_070.htm", group = "Data Elements", sub = true },
            new { id = "e071", label = "> 071-080 Addresses & Activity", url = BaseUrl + "DED_071_080.htm", group = "Data Elements", sub = true },
            new { id = "e081", label = "> 081-090 Qualification Detail", url = BaseUrl + "Ded_081_090.htm", group = "Data Elements", sub = true },
            new { id = "e091", label = "> 091-100 NQF & Post Doctoral", url = BaseUrl + "Ded_091_100.htm", group = "Data Elements", sub = true },
            new { id = "e101", label = "> 101-106 Funding & Foundation", url = BaseUrl + "Ded_101_110.htm", group = "Data Elements", sub = true },
            new { id = "space201", label = "> 201-226 Building Space", url = BaseUrl + "DedSpace_201_210.htm", group = "Data Elements", sub = true },
            new { id = "ded_deriv", label = "(H) Derived Elements", url = BaseUrl + "DEDDeriv.htm", group = "Data Elements", sub = false },
            new { id = "glossary", label = "(I) Glossary", url = BaseUrl + "Glossary.htm", group = "Reference", sub = false },
            new { id = "credvals", label = "(J) Credit Values", url = BaseUrl + "CredVals.htm", group = "Reference", sub = false },
            new { id = "edits", label = "(K) Edit Validation Rules", url = BaseUrl + "Edits.htm", group = "Reference", sub = false },
            new { id = "valpac", label = "(L) Using Valpac.Net", url = BaseUrl + "Valpac.htm", group = "Reference", sub = false },
            new { id = "cesm", label = "(M) CESM Codes", url = BaseUrl + "CESM.htm", group = "Reference", sub = false },
            new { id = "circulars", label = "(Q) Circulars", url = BaseUrl + "Circulars.htm", group = "Reference", sub = false },
            new { id = "audit08", label = "(R) Audit Directives Feb 2008", url = BaseUrl + "Audit_directives_Feb08.htm", group = "Directives", sub = false },
            new { id = "audit09", label = "(S) Audit Directives Apr 2009", url = BaseUrl + "Audit_directives_Apr09.htm", group = "Directives", sub = false }
        };

        return Json(pages);
    }

    private static string ResolveAbsoluteUrl(string pageUrl, string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return candidate;

        if (candidate.StartsWith("//", StringComparison.Ordinal))
            return "https:" + candidate;

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var absoluteUri))
            return absoluteUri.ToString();

        if (Uri.TryCreate(pageUrl, UriKind.Absolute, out var pageUri) &&
            Uri.TryCreate(pageUri, candidate, out var resolvedUri))
            return resolvedUri.ToString();

        return candidate;
    }
}
