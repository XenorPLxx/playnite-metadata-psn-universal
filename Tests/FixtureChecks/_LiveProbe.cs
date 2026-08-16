// Temporary live check: build the URL with the plugin's own builder, fetch it, parse with the
// plugin's own parser.
using System.Net.Http;
using System.Net.Http.Headers;
using UniversalPSNMetadata;

internal static class LiveProbe
{
    internal static async Task RunAsync(string term, string locale)
    {
        var url = UniversalPSNMetadataGameSession.BuildSearchUrl(UniversalPSNMetadataGameSession.NormalizeGameName(term), locale);
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        // Use the plugin's own header set so this probe fails whenever the real request would.
        UniversalPSNMetadataGameSession.ConfigureStoreRequest(request.Headers, locale);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"  HTTP {(int)response.StatusCode}");
        var results = UniversalPSNMetadataGameSession.ParseSearchResults(body, locale, out var error);
        Console.WriteLine($"  parsed={results.Count} error={error ?? "(none)"}");
        foreach (var r in results.Take(3))
        {
            Console.WriteLine($"    {r.Name}  cover={r.CoverUrl?.Split('/')[^1]}  class={r.StoreDisplayClassification}");
        }
        var match = UniversalPSNMetadataGameSession.GetMatchingGame(term, results, ["PlayStation 5"]);
        Console.WriteLine($"  auto-match: {match?.Name ?? "(none)"}");
    }
}
