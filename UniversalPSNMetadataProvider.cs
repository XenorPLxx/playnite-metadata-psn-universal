using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Playnite;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UniversalPSNMetadata;

public sealed class UniversalPSNMetadataProvider : MetadataProvider
{
    private readonly IPlayniteApi playniteApi;
    private readonly Plugin.GetMetadataProviderArgs metadataArgs;
    private readonly string storeLocale;

    public UniversalPSNMetadataProvider(IPlayniteApi playniteApi, Plugin.GetMetadataProviderArgs metadataArgs, string storeLocale)
    {
        this.playniteApi = playniteApi;
        this.metadataArgs = metadataArgs;
        this.storeLocale = storeLocale;
    }

    public override Task<MetadataProviderGameSession?> CreateGameSessionAsync(CreateGameMetadataSessionArgs args)
    {
        return Task.FromResult<MetadataProviderGameSession?>(
            new UniversalPSNMetadataGameSession(playniteApi, metadataArgs, storeLocale, args.Game));
    }
}

internal sealed class UniversalPSNMetadataGameSession : MetadataProviderGameSession
{
    private static readonly ILogger logger = LogManager.GetLogger();
    private static readonly HttpClient httpClient = new();
    private static readonly TimeSpan requestTimeout = TimeSpan.FromSeconds(30);

    private readonly IPlayniteApi playniteApi;
    private readonly Plugin.GetMetadataProviderArgs metadataArgs;
    private readonly string storeLocale;
    private Task<StoreSearchResult?>? selectionTask;
    private Task<StorePageMetadata?>? pageMetadataTask;

    public UniversalPSNMetadataGameSession(
        IPlayniteApi playniteApi,
        Plugin.GetMetadataProviderArgs metadataArgs,
        string storeLocale,
        Game game) : base(game)
    {
        this.playniteApi = playniteApi;
        this.metadataArgs = metadataArgs;
        this.storeLocale = storeLocale;
    }

    public override async Task<object?> GetDataAsync(GetDataArgs dataArgs)
    {
        switch (dataArgs.DataId)
        {
            case BuiltInGameDataId.DesktopCover:
                return await GetCoverAsync(dataArgs.CancelToken);
            case BuiltInGameDataId.DesktopIcon:
                return await GetIconAsync(dataArgs.CancelToken);
            case BuiltInGameDataId.DesktopBackground:
                return await GetBackgroundAsync(dataArgs.CancelToken);
            case BuiltInGameDataId.Description:
                return await GetDescriptionAsync(dataArgs.CancelToken);
            case BuiltInGameDataId.Genres:
                return await GetGenresAsync(dataArgs.CancelToken);
            case BuiltInGameDataId.Publishers:
                return await GetPublishersAsync(dataArgs.CancelToken);
            case BuiltInGameDataId.ReleaseDate:
                return await GetReleaseDateAsync(dataArgs.CancelToken);
            case BuiltInGameDataId.CommunityScore:
                return await GetCommunityScoreAsync(dataArgs.CancelToken);
            case BuiltInGameDataId.ExternalIds:
                return await GetStoreIdAsync(dataArgs.CancelToken);
            case BuiltInGameDataId.Links:
                return await GetLinksAsync(dataArgs.CancelToken);
            default:
                return null;
        }
    }

    private async Task<ImportableFile?> GetCoverAsync(CancellationToken cancelToken)
    {
        var game = await GetSelectedGameAsync(cancelToken);
        return string.IsNullOrWhiteSpace(game?.CoverUrl) ? null : new ImportableFile(BuiltInGameDataId.DesktopCover, game.CoverUrl);
    }

    private async Task<ImportableFile?> GetIconAsync(CancellationToken cancelToken)
    {
        var game = await GetSelectedGameAsync(cancelToken);
        return string.IsNullOrWhiteSpace(game?.CoverUrl) ? null : new ImportableFile(BuiltInGameDataId.DesktopIcon, game.CoverUrl);
    }

    private async Task<ImportableFile?> GetBackgroundAsync(CancellationToken cancelToken)
    {
        var game = await GetSelectedGameAsync(cancelToken);
        if (!string.IsNullOrWhiteSpace(game?.BackgroundUrl))
        {
            return new ImportableFile(BuiltInGameDataId.DesktopBackground, game.BackgroundUrl);
        }

        var metadata = await GetStorePageMetadataAsync(cancelToken);
        return string.IsNullOrWhiteSpace(metadata?.BackgroundImageUrl)
            ? null
            : new ImportableFile(BuiltInGameDataId.DesktopBackground, metadata.BackgroundImageUrl);
    }

    private async Task<GameDescription?> GetDescriptionAsync(CancellationToken cancelToken)
    {
        var metadata = await GetStorePageMetadataAsync(cancelToken);
        return string.IsNullOrWhiteSpace(metadata?.Description)
            ? null
            : new GameDescription(metadata.Description, GameDescriptionFormat.HTML);
    }

    private async Task<List<NameImportableProperty>?> GetGenresAsync(CancellationToken cancelToken)
    {
        var metadata = await GetStorePageMetadataAsync(cancelToken);
        return metadata?.Genres.Count > 0
            ? metadata.Genres.Select(genre => new NameImportableProperty(genre)).ToList()
            : null;
    }

    private async Task<List<NameImportableProperty>?> GetPublishersAsync(CancellationToken cancelToken)
    {
        var metadata = await GetStorePageMetadataAsync(cancelToken);
        return string.IsNullOrWhiteSpace(metadata?.Publisher)
            ? null
            : [new NameImportableProperty(metadata.Publisher)];
    }

    private async Task<PartialDate?> GetReleaseDateAsync(CancellationToken cancelToken)
    {
        var metadata = await GetStorePageMetadataAsync(cancelToken);
        return metadata?.ReleaseDate;
    }

    private async Task<int?> GetCommunityScoreAsync(CancellationToken cancelToken)
    {
        var metadata = await GetStorePageMetadataAsync(cancelToken);
        return metadata?.CommunityScore;
    }

    private async Task<ImportableExternalIdentifier?> GetStoreIdAsync(CancellationToken cancelToken)
    {
        var game = await GetSelectedGameAsync(cancelToken);
        return string.IsNullOrWhiteSpace(game?.StoreId)
            ? null
            : new ImportableExternalIdentifier(
                UniversalPSNMetadataPlugin.ExternalIdType,
                UniversalPSNMetadataPlugin.ExternalIdName,
                game.StoreId);
    }

    private async Task<List<ImportableWebLink>?> GetLinksAsync(CancellationToken cancelToken)
    {
        var game = await GetSelectedGameAsync(cancelToken);
        return string.IsNullOrWhiteSpace(game?.GameUrl)
            ? null
            : [new ImportableWebLink("playstation.store", "PlayStation Store", game.GameUrl)];
    }

    private async Task<StoreSearchResult?> GetSelectedGameAsync(CancellationToken cancelToken)
    {
        if (selectionTask is { IsCompletedSuccessfully: true })
        {
            return await selectionTask;
        }

        var task = ResolveSelectedGameAsync(cancelToken);
        selectionTask = task;
        var result = await task;
        return result;
    }

    private async Task<StoreSearchResult?> ResolveSelectedGameAsync(CancellationToken cancelToken)
    {
        if (metadataArgs.Type == MetadataDownloadType.BackgroundDownload)
        {
            try
            {
                var results = await SearchStoreAsync(Game.Name, cancelToken);
                return GetMatchingGame(Game.Name, results, GetGamePlatformNames());
            }
            catch (OperationCanceledException) when (cancelToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                logger.Error(e, $"Failed to search the PlayStation Store for {Game.Name}.");
                return null;
            }
        }

        var platforms = GetGamePlatformNames();
        try
        {
            var item = await playniteApi.Dialogs.ChooseItemWithSearchAsync(
                Game.Name,
                async args =>
                {
                    if (string.IsNullOrWhiteSpace(args.SearchTerm))
                    {
                        return [];
                    }

                    try
                    {
                        var results = await SearchStoreAsync(args.SearchTerm, args.CancelToken);
                        // Present the best match first rather than raw Store order.
                        return SortByMatch(args.SearchTerm, results, platforms).Cast<ChooseDialogItem>().ToList();
                    }
                    catch (OperationCanceledException) when (args.CancelToken.IsCancellationRequested)
                    {
                        return [];
                    }
                    catch (Exception e)
                    {
                        logger.Error(e, $"Failed to search the PlayStation Store for {args.SearchTerm}.");
                        return [];
                    }
                },
                Loc.psnstore_search_caption());
            return item as StoreSearchResult;
        }
        catch (OperationCanceledException) when (cancelToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.Error(e, $"The PlayStation Store picker failed for {Game.Name}.");
            return null;
        }
    }

    /// <summary>
    /// The game's platform names, used only to prefer Store products sold for them. The game holds
    /// platform ids, so the names have to be looked up in the library.
    /// </summary>
    private List<string> GetGamePlatformNames()
    {
        if (Game.PlatformIds is not { Count: > 0 })
        {
            return [];
        }

        try
        {
            return playniteApi.Library.Platforms
                .Get(Game.PlatformIds)
                .Select(platform => platform?.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .ToList();
        }
        catch (Exception e)
        {
            // Platform names only refine the ranking, so losing them must not fail the lookup.
            logger.Error(e, "Failed to read platform names for PlayStation Store matching.");
            return [];
        }
    }

    private async Task<StorePageMetadata?> GetStorePageMetadataAsync(CancellationToken cancelToken)
    {
        if (pageMetadataTask is { IsCompletedSuccessfully: true })
        {
            return await pageMetadataTask;
        }

        var task = ResolveStorePageMetadataAsync(cancelToken);
        pageMetadataTask = task;
        return await task;
    }

    private async Task<StorePageMetadata?> ResolveStorePageMetadataAsync(CancellationToken cancelToken)
    {
        var game = await GetSelectedGameAsync(cancelToken);
        if (string.IsNullOrWhiteSpace(game?.GameUrl))
        {
            return null;
        }

        try
        {
            return ParseStorePageMetadata(await DownloadStoreStringAsync(game.GameUrl, cancelToken));
        }
        catch (OperationCanceledException) when (cancelToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.Warn(e, $"Failed to retrieve PlayStation Store product page {game.GameUrl}.");
            return null;
        }
    }

    private async Task<List<StoreSearchResult>> SearchStoreAsync(string searchTerm, CancellationToken cancelToken)
    {
        var normalizedName = NormalizeGameName(searchTerm);
        var response = await DownloadStoreStringAsync(BuildSearchUrl(normalizedName, storeLocale), cancelToken);
        var results = ParseSearchResults(response, storeLocale, out var searchError);
        if (!string.IsNullOrWhiteSpace(searchError))
        {
            logger.Warn($"PlayStation Store search returned an API error for {normalizedName}: {searchError}");
        }

        return results;
    }

    private async Task<string> DownloadStoreStringAsync(string url, CancellationToken cancelToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
        timeout.CancelAfter(requestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ConfigureStoreRequest(request.Headers, storeLocale);
        using var response = await httpClient.SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(timeout.Token);
    }

    internal static StorePageMetadata? ParseStorePageMetadata(string pageSource)
    {
        if (string.IsNullOrWhiteSpace(pageSource)) return null;
        var page = new HtmlParser().ParseDocument(pageSource);
        return new StorePageMetadata
        {
            Description = GetDescription(page),
            BackgroundImageUrl = GetImageUrl(page, "img[data-qa='gameBackgroundImage#heroImage#image-no-js']", "img[data-qa='gameBackgroundImage#heroImage#preview']"),
            Publisher = GetText(page, "[data-qa='gameInfo#releaseInformation#publisher-value']", "[data-qa='mfe-game-title#publisher']"),
            Genres = GetText(page, "[data-qa='gameInfo#releaseInformation#genre-value']")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList() ?? [],
            ReleaseDate = GetReleaseDate(pageSource, page),
            CommunityScore = GetCommunityScore(page)
        };
    }

    internal static List<StoreSearchResult> ParseSearchResults(string response, string storeLocale, out string? searchError)
    {
        searchError = null;
        using var document = JsonDocument.Parse(response);
        var root = document.RootElement;
        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            searchError = string.Join("; ", errors.EnumerateArray()
                .Where(error => error.TryGetProperty("message", out _))
                .Select(error => error.GetProperty("message").GetString())
                .Where(message => !string.IsNullOrWhiteSpace(message)));
            if (string.IsNullOrWhiteSpace(searchError))
            {
                searchError = null;
            }
        }

        if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("universalSearch", out var search) ||
            !search.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            searchError ??= "The response did not contain universal search data. The Store API may have changed.";
            return [];
        }

        var parsed = new List<StoreSearchResult>();
        foreach (var result in results.EnumerateArray())
        {
            var name = GetJsonString(result, "name");
            var id = GetJsonString(result, "id");
            var cover = GetMediaUrl(result, "MASTER", "PORTRAIT_BANNER", "EDITION_KEY_ART", "GAMEHUB_COVER_ART") ?? GetAnyImageUrl(result);
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(cover)) continue;

            var platforms = result.TryGetProperty("platforms", out var platformsElement) && platformsElement.ValueKind == JsonValueKind.Array
                ? platformsElement.EnumerateArray().Select(platform => platform.GetString()).Where(platform => !string.IsNullOrWhiteSpace(platform)).Cast<string>().ToList()
                : [];
            var classification = GetJsonString(result, "localizedStoreDisplayClassification");
            var description = string.Join(" · ", new[] { classification, platforms.Count > 0 ? string.Join(", ", platforms) : null }.Where(value => !string.IsNullOrWhiteSpace(value)));
            var route = string.Equals(GetJsonString(result, "__typename"), "Concept", StringComparison.OrdinalIgnoreCase) ? "concept" : "product";
            parsed.Add(new StoreSearchResult(name, description)
            {
                CoverUrl = cover,
                BackgroundUrl = GetMediaUrl(result, "BACKGROUND", "SIXTEEN_BY_NINE_BANNER"),
                StoreDisplayClassification = GetJsonString(result, "storeDisplayClassification"),
                Platforms = platforms,
                StoreId = id,
                GameUrl = $"https://store.playstation.com/{StoreLocaleOptions.GetValidOrDefault(storeLocale)}/{route}/{id}"
            });
        }

        return parsed;
    }

    internal static string BuildSearchUrl(string searchTerm, string storeLocale)
    {
        var locale = StoreLocaleOptions.GetValidOrDefault(storeLocale).Split('-');
        var language = locale[0] == "zh" && locale.Length > 2 && locale[1] == "hant" ? "ch" : locale[0];
        var variables = JsonSerializer.Serialize(new { countryCode = locale[^1].ToUpperInvariant(), languageCode = language.ToLowerInvariant(), nextCursor = "", pageOffset = 0, pageSize = 24, searchTerm });
        const string queryHash = "4df6284f982e57bec70f23c77e2c219dc792eb19af7fb3d3a81767aa3f1958aa";
        var extensions = $"{{\"persistedQuery\":{{\"version\":1,\"sha256Hash\":\"{queryHash}\"}}}}";
        return $"https://web.np.playstation.com/api/graphql/v1//op?operationName=getSearchResults&variables={Uri.EscapeDataString(variables)}&extensions={Uri.EscapeDataString(extensions)}";
    }

    internal static StoreSearchResult? GetMatchingGame(string gameName, IEnumerable<StoreSearchResult> results, IEnumerable<string>? gamePlatforms = null)
    {
        var scored = ScoreResults(gameName, results, gamePlatforms);
        if (scored.Count == 0)
        {
            logger.Debug($"No PlayStation Store candidate scored above zero for '{gameName}'.");
            return null;
        }

        if (scored.Count > 1 && scored[0].Score < 600 && scored[0].Score - scored[1].Score < 25)
        {
            logger.Debug(
                $"Ambiguous PlayStation Store match for '{gameName}': " +
                $"'{scored[0].Result.Name}' ({scored[0].Score}) vs '{scored[1].Result.Name}' ({scored[1].Score}). Skipping.");
            return null;
        }

        logger.Debug($"Matched '{gameName}' to PlayStation Store '{scored[0].Result.Name}' with score {scored[0].Score}.");
        return scored[0].Result;
    }

    /// <summary>Orders results best-match first, keeping unscored ones after the scored ones.</summary>
    internal static List<StoreSearchResult> SortByMatch(string gameName, IEnumerable<StoreSearchResult> results, IEnumerable<string>? gamePlatforms = null) =>
        results
            .Select(result => (Result: result, Score: GetMatchScore(gameName, result, gamePlatforms)))
            // OrderByDescending is stable, so equally scored results keep the Store's own relevance
            // order. That matters most for a partial search term, where nothing scores at all.
            .OrderByDescending(item => item.Score)
            .Select(item => item.Result)
            .ToList();

    private static List<(StoreSearchResult Result, int Score)> ScoreResults(string gameName, IEnumerable<StoreSearchResult> results, IEnumerable<string>? gamePlatforms) =>
        results
            .Select(result => (Result: result, Score: GetMatchScore(gameName, result, gamePlatforms)))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Result.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Rewards a candidate that is sold for a platform the game is tagged with, and penalises one
    /// that is not. Without this a PlayStation 3 game can match a PlayStation 5 only product.
    /// </summary>
    private static int GetPlatformScore(IEnumerable<string>? gamePlatforms, List<string> resultPlatforms)
    {
        var requested = gamePlatforms?
            .Select(GetPlayStationPlatform)
            .Where(platform => !string.IsNullOrEmpty(platform))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (requested is not { Count: > 0 } || resultPlatforms.Count == 0)
        {
            return 0;
        }

        return resultPlatforms.Any(platform => requested.Contains(platform, StringComparer.OrdinalIgnoreCase)) ? 60 : -40;
    }

    private static string? GetPlayStationPlatform(string? platformName)
    {
        if (string.IsNullOrEmpty(platformName))
        {
            return null;
        }

        return platformName switch
        {
            _ when Contains(platformName, "PlayStation 5") || Equals(platformName, "PS5") => "PS5",
            _ when Contains(platformName, "PlayStation 4") || Equals(platformName, "PS4") => "PS4",
            _ when Contains(platformName, "PlayStation VR") || Equals(platformName, "PS VR") => "PS VR",
            _ when Contains(platformName, "PlayStation 3") || Equals(platformName, "PS3") => "PS3",
            _ when Contains(platformName, "PlayStation Vita") || Equals(platformName, "PS Vita") => "PS Vita",
            _ when Contains(platformName, "PlayStation Portable") || Equals(platformName, "PSP") => "PSP",
            _ => null
        };

        static bool Contains(string value, string part) => value.Contains(part, StringComparison.OrdinalIgnoreCase);
        static bool Equals(string value, string other) => string.Equals(value, other, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetMatchScore(string gameName, StoreSearchResult result, IEnumerable<string>? gamePlatforms = null)
    {
        if (result is null) return 0;
        var requested = GetComparisonTitle(gameName);
        var candidate = GetComparisonTitle(result.Name);
        if (string.IsNullOrWhiteSpace(requested) || string.IsNullOrWhiteSpace(candidate)) return 0;
        var requestedQualifiers = GetEditionQualifiers(requested);
        var candidateQualifiers = GetEditionQualifiers(candidate);
        var exact = string.Equals(requested, candidate, StringComparison.OrdinalIgnoreCase);
        var baseMatch = string.Equals(RemoveEditionQualifiers(requested), RemoveEditionQualifiers(candidate), StringComparison.OrdinalIgnoreCase);
        if (!exact && !baseMatch) return 0;

        var score = exact ? 1000 : 700;
        var missingRequestedQualifiers = requestedQualifiers.Except(candidateQualifiers).Count();
        score += requestedQualifiers.Count > 0 && missingRequestedQualifiers == 0 ? 200 : 0;
        score -= 130 * missingRequestedQualifiers;
        score -= 200 * candidateQualifiers.Except(requestedQualifiers).Count();
        score += GetPlatformScore(gamePlatforms, result.Platforms);
        score += result.StoreDisplayClassification switch { "FULL_GAME" => 120, "GAME_BUNDLE" => -120, "PREMIUM_EDITION" => requestedQualifiers.Count > 0 ? 40 : -160, "ADD_ON" or "ADD_ON_PACK" or "CHARACTER" or "COSTUME" or "GAME_LEVEL" or "ITEM" or "VIRTUAL_CURRENCY" or "DEMO" => -500, _ => 0 };
        return score;
    }

    private static string GetComparisonTitle(string? title)
    {
        var value = NormalizeGameName(title).Replace("&", " and ").Replace("'", string.Empty).ToLowerInvariant();
        value = Regex.Replace(value, @"(?:\s*(?:and|&|,|/|-)?\s*(?:ps\s*[345]|playstation\s*[345]|ps\s*vr(?:\s*2)?|playstation\s*vr(?:\s*2)?))+\s*$", "", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\b([1-9]|[12]\d|30)\b", match => ToRoman(int.Parse(match.Value)).ToLowerInvariant());
        value = Regex.Replace(value, @"[^a-z0-9]+", " ");
        return Regex.Replace(Regex.Replace(value, @"\s+", " ").Trim(), @"^the\s+", string.Empty);
    }

    /// <summary>
    /// Collects the edition qualifiers in a title as canonical tokens, so that "remastered" and
    /// "remaster", or "deluxe edition" and "deluxe", count as the same qualifier rather than as two
    /// different ones that penalise each other.
    /// </summary>
    private static HashSet<string> GetEditionQualifiers(string title) =>
        EditionQualifierAliases
            .Where(alias => Regex.IsMatch(title, @"\b" + Regex.Escape(alias.Key) + @"\b", RegexOptions.IgnoreCase))
            .Select(alias => alias.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string RemoveEditionQualifiers(string title)
    {
        foreach (var qualifier in EditionQualifiers) title = Regex.Replace(title, @"\b" + Regex.Escape(qualifier) + @"\b", " ", RegexOptions.IgnoreCase);
        return Regex.Replace(Regex.Replace(title, @"\bedition\b", " ", RegexOptions.IgnoreCase), @"\s+", " ").Trim();
    }

    /// <summary>
    /// Surface form to canonical token. Longer forms come first so that "deluxe edition" is matched
    /// and removed before the bare "deluxe" it contains.
    /// </summary>
    private static readonly Dictionary<string, string> EditionQualifierAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["directors cut"] = "directors cut",
        ["game of the year"] = "game of the year",
        ["digital deluxe"] = "digital deluxe",
        ["deluxe edition"] = "deluxe",
        ["complete edition"] = "complete",
        ["definitive edition"] = "definitive",
        ["ultimate edition"] = "ultimate",
        ["special edition"] = "special edition",
        ["anniversary edition"] = "anniversary edition",
        ["premium edition"] = "premium",
        ["enhanced edition"] = "enhanced",
        ["gold edition"] = "gold edition",
        ["platinum edition"] = "platinum edition",
        ["collectors edition"] = "collectors edition",
        ["standard edition"] = "standard",
        ["remastered"] = "remaster",
        ["remaster"] = "remaster",
        ["remake"] = "remake",
        ["enhanced"] = "enhanced",
        ["definitive"] = "definitive",
        ["complete"] = "complete",
        ["deluxe"] = "deluxe",
        ["ultimate"] = "ultimate",
        ["premium"] = "premium"
    };

    private static readonly string[] EditionQualifiers = [.. EditionQualifierAliases.Keys];
    private static readonly (int Value, string Name)[] RomanNumerals = [(1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")];

    private static string ToRoman(int value)
    {
        var result = new System.Text.StringBuilder();
        foreach (var (number, roman) in RomanNumerals) while (value >= number) { result.Append(roman); value -= number; }
        return result.ToString();
    }

    internal static void ConfigureStoreRequest(HttpRequestHeaders headers, string storeLocale)
    {
        headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Safari/537.36");
        headers.Referrer = new Uri("https://store.playstation.com/");
        headers.Add("Origin", "https://store.playstation.com");
        // Apollo rejects a request as potential CSRF unless it carries either a non-simple
        // content-type or one of these headers. A GET has no content, so the header is the honest
        // way to satisfy it; removing it makes every search fail with CSRF_ERROR.
        headers.Add("x-apollo-operation-name", "getSearchResults");
        headers.Add("apollographql-client-name", "@sie-ppr-web-store/app");
        headers.Add("apollographql-client-version", "0.113.0");
        headers.Add("X-PSN-App-Ver", "@sie-ppr-web-store/app/0.113.0-");
        headers.Add("X-PSN-Correlation-ID", Guid.NewGuid().ToString());
        headers.Add("X-PSN-Request-ID", Guid.NewGuid().ToString());
        headers.Add("X-PSN-Store-Locale-Override", ToStoreLocaleHeader(storeLocale));
    }

    private static string ToStoreLocaleHeader(string locale)
    {
        var parts = StoreLocaleOptions.GetValidOrDefault(locale).Split('-');
        return string.Join("-", parts.Select((part, index) => index == parts.Length - 1 ? part.ToUpperInvariant() : part.ToLowerInvariant()));
    }

    internal static string NormalizeGameName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var normalized = Regex.Replace(name, "[™©®]", "").Replace("_", " ").Replace(".", " ").Replace('’', '\'');
        var withoutBrackets = Regex.Replace(normalized, @"\[.*?\]", "");
        normalized = string.IsNullOrWhiteSpace(withoutBrackets) ? normalized : withoutBrackets;
        var withoutParentheses = Regex.Replace(normalized, @"\(.*?\)", "");
        normalized = string.IsNullOrWhiteSpace(withoutParentheses) ? normalized : withoutParentheses;
        normalized = Regex.Replace(normalized, @"\s*:\s*", ": ");
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        return Regex.IsMatch(normalized, @",\s*The$", RegexOptions.IgnoreCase) ? "The " + Regex.Replace(normalized, @",\s*The$", "", RegexOptions.IgnoreCase) : normalized;
    }

    private static string? GetJsonString(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string? GetMediaUrl(JsonElement result, params string[] roles)
    {
        if (!result.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        // The roles are a preference order. Scanning the media array once and accepting any matching
        // role would take whichever happens to come first, and MASTER is always last.
        foreach (var role in roles)
        {
            foreach (var item in media.EnumerateArray())
            {
                if (string.Equals(GetJsonString(item, "type"), "IMAGE", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(GetJsonString(item, "role"), role, StringComparison.OrdinalIgnoreCase))
                {
                    var url = GetJsonString(item, "url");
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        return url;
                    }
                }
            }
        }

        return null;
    }
    private static string? GetAnyImageUrl(JsonElement result) =>
        result.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Array
            ? media.EnumerateArray()
                .Where(item => string.Equals(GetJsonString(item, "type"), "IMAGE", StringComparison.OrdinalIgnoreCase))
                .Select(item => GetJsonString(item, "url"))
                .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url))
            : null;
    private static string? GetPropertyOrNull(JsonElement element, string name) => element.ValueKind != JsonValueKind.Undefined ? GetJsonString(element, name) : null;
    private static string? GetDescription(IHtmlDocument page) => page.QuerySelector("[data-qa='mfe-game-overview#description']")?.InnerHtml ?? page.QuerySelector("p.psw-c-bg-card-1")?.InnerHtml ?? page.QuerySelector("meta[name='description']")?.GetAttribute("content");
    private static string? GetText(IHtmlDocument page, params string[] selectors) => selectors.Select(selector => page.QuerySelector(selector)?.TextContent?.Trim()).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    private static string? GetImageUrl(IHtmlDocument page, params string[] selectors) => selectors.Select(selector => page.QuerySelector(selector)?.GetAttribute("src")?.Split('?')[0]).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    private static PartialDate? GetReleaseDate(string source, IHtmlDocument page)
    {
        var match = Regex.Match(source, "\\\"releaseDate\\\"\\s*:\\s*\\\"(?<date>\\d{4}-\\d{2}-\\d{2})");
        if (match.Success && PartialDate.TryParse(match.Groups["date"].Value, out var date)) return date;
        return PartialDate.TryParse(GetText(page, "[data-qa='gameInfo#releaseInformation#releaseDate-value']"), out date) ? date : null;
    }
    private static int? GetCommunityScore(IHtmlDocument page) => double.TryParse(GetText(page, "[data-qa='mfe-game-title#average-rating']"), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rating) && rating is >= 0 and <= 5 ? (int)Math.Round(rating * 20, MidpointRounding.AwayFromZero) : null;
}

internal sealed class StoreSearchResult : ChooseDialogItem
{
    public StoreSearchResult(string name, string? description) : base(name, description) { }
    public string? CoverUrl { get; init; }
    public string? BackgroundUrl { get; init; }
    public string? StoreDisplayClassification { get; init; }
    public List<string> Platforms { get; init; } = [];
    public string? StoreId { get; init; }
    public string? GameUrl { get; init; }
}

internal sealed class StorePageMetadata
{
    public string? Description { get; init; }
    public string? BackgroundImageUrl { get; init; }
    public List<string> Genres { get; init; } = [];
    public string? Publisher { get; init; }
    public PartialDate? ReleaseDate { get; init; }
    public int? CommunityScore { get; init; }
}
