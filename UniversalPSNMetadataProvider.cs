using AngleSharp.Dom.Html;
using AngleSharp.Parser.Html;
using Playnite.Common;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalPSNMetadata
{
  public class UniversalPSNMetadataProvider : OnDemandMetadataProvider
  {
    private readonly MetadataRequestOptions options;
    private readonly UniversalPSNMetadata plugin;
    private static readonly ILogger logger = LogManager.GetLogger();
    private MetadataFile cover;
    private MetadataFile background;
    private string gameUrl;
    private string gamePageSource;
    private IHtmlDocument gamePage;
    private StorePageMetadata storePageMetadata;
    private const string SearchUrl = "https://web.np.playstation.com/api/graphql/v1//op";
    private const string SearchQueryHash = "4df6284f982e57bec70f23c77e2c219dc792eb19af7fb3d3a81767aa3f1958aa";
    private const string DefaultStoreLocale = StoreLocaleOptions.DefaultLocale;
    private const string StoreApplicationName = "@sie-ppr-web-store/app";
    private const string StoreApplicationVersion = "0.113.0";
    private static readonly TimeSpan StoreRequestTimeout = TimeSpan.FromSeconds(30);
    private const int WeakMatchScore = 600;
    private const int AmbiguousMatchScoreDifference = 25;
    private static readonly Dictionary<string, string> EditionQualifierAliases = new Dictionary<string, string>
    {
      { "directors cut", "directors cut" },
      { "game of the year", "game of the year" },
      { "digital deluxe", "digital deluxe" },
      { "deluxe edition", "deluxe" },
      { "complete edition", "complete" },
      { "definitive edition", "definitive" },
      { "ultimate edition", "ultimate" },
      { "special edition", "special edition" },
      { "anniversary edition", "anniversary edition" },
      { "premium edition", "premium" },
      { "enhanced edition", "enhanced" },
      { "gold edition", "gold edition" },
      { "platinum edition", "platinum edition" },
      { "collectors edition", "collectors edition" },
      { "standard edition", "standard" },
      { "remastered", "remaster" },
      { "remaster", "remaster" },
      { "remake", "remake" },
      { "enhanced", "enhanced" },
      { "definitive", "definitive" },
      { "complete", "complete" },
      { "deluxe", "deluxe" },
      { "ultimate", "ultimate" },
      { "premium", "premium" }
    };

    public override List<MetadataField> AvailableFields { get; } = new List<MetadataField>
        {
            MetadataField.Description,
            MetadataField.BackgroundImage,
            MetadataField.CommunityScore,
            MetadataField.CoverImage,
            //MetadataField.CriticScore,
            //MetadataField.Developers,
            MetadataField.Genres,
            MetadataField.Icon,
            MetadataField.Links,
            MetadataField.Publishers,
            MetadataField.ReleaseDate,
            //MetadataField.Features,
            //MetadataField.Name,
            //MetadataField.Platform,
            //MetadataField.Series
        };

    public UniversalPSNMetadataProvider(MetadataRequestOptions options, UniversalPSNMetadata plugin)
    {
      this.options = options;
      this.plugin = plugin;
    }

    private string StoreLocale => StoreLocaleOptions.GetOrDefault(plugin?.StoreLocale);

    public override MetadataFile GetCoverImage(GetMetadataFieldArgs args)
    {
      GetSearchResults(options.GameData.Name, args.CancelToken);
      if (cover != null)
      {
        return cover;
      }
      return base.GetCoverImage(args);
    }

    // Covers are square, so might be useful as icons
    public override MetadataFile GetIcon(GetMetadataFieldArgs args)
    {
      GetSearchResults(options.GameData.Name, args.CancelToken);
      if (cover != null)
      {
        return cover;
      }
      return base.GetIcon(args);
    }

    public override MetadataFile GetBackgroundImage(GetMetadataFieldArgs args)
    {
      GetSearchResults(options.GameData.Name, args.CancelToken);
      if (background != null)
      {
        return background;
      }

      if (!string.IsNullOrEmpty(gameUrl))
      {
        var backgroundImageUrl = GetStorePageMetadata(args.CancelToken)?.BackgroundImageUrl;
        if (!string.IsNullOrEmpty(backgroundImageUrl))
        {
          return new MetadataFile(backgroundImageUrl);
        }
      }
      return base.GetBackgroundImage(args);
    }


    public override string GetDescription(GetMetadataFieldArgs args)
    {
      GetSearchResults(options.GameData.Name, args.CancelToken);
      if (!string.IsNullOrEmpty(gameUrl))
      {
        var metadata = GetStorePageMetadata(args.CancelToken);
        if (!string.IsNullOrEmpty(metadata?.Description))
        {
          return metadata.Description;
        }
      }
      return base.GetDescription(args);
    }

    public override IEnumerable<MetadataProperty> GetGenres(GetMetadataFieldArgs args)
    {
      GetSearchResults(options.GameData.Name, args.CancelToken);
      return GetStorePageMetadata(args.CancelToken)?.Genres.Select(genre => new MetadataNameProperty(genre))
        ?? base.GetGenres(args);
    }

    public override IEnumerable<MetadataProperty> GetPublishers(GetMetadataFieldArgs args)
    {
      GetSearchResults(options.GameData.Name, args.CancelToken);
      var publisher = GetStorePageMetadata(args.CancelToken)?.Publisher;
      if (!string.IsNullOrEmpty(publisher))
      {
        return new[] { new MetadataNameProperty(publisher) };
      }

      return base.GetPublishers(args);
    }

    public override ReleaseDate? GetReleaseDate(GetMetadataFieldArgs args)
    {
      GetSearchResults(options.GameData.Name, args.CancelToken);
      return GetStorePageMetadata(args.CancelToken)?.ReleaseDate ?? base.GetReleaseDate(args);
    }

    public override int? GetCommunityScore(GetMetadataFieldArgs args)
    {
      GetSearchResults(options.GameData.Name, args.CancelToken);
      return GetStorePageMetadata(args.CancelToken)?.CommunityScore ?? base.GetCommunityScore(args);
    }

    public override IEnumerable<Link> GetLinks(GetMetadataFieldArgs args)
    {
      GetSearchResults(options.GameData.Name, args.CancelToken);
      if (!string.IsNullOrEmpty(gameUrl))
      {
        return new[] { new Link("PlayStation Store", gameUrl) };
      }

      return base.GetLinks(args);
    }

    internal void GetGameData()
    {

    }

    public class StoreSearchResult : GenericItemOption
    {
      public string CoverUrl { get; set; }
      public string BackgroundUrl { get; set; }
      public string StoreDisplayClassification { get; set; }
      public List<string> Platforms { get; set; }
      public string GameUrl { get; set; }
    }

    internal class StorePageMetadata
    {
      public string Description { get; set; }
      public string BackgroundImageUrl { get; set; }
      public List<string> Genres { get; set; }
      public string Publisher { get; set; }
      public ReleaseDate? ReleaseDate { get; set; }
      public int? CommunityScore { get; set; }
    }

    public void GetSearchResults(string searchTerm)
    {
      GetSearchResults(searchTerm, CancellationToken.None);
    }

    private void GetSearchResults(string searchTerm, CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (gameUrl != null) { return; }
      var normalizedSearchTerm = StringExtensions.NormalizeGameName(searchTerm);
      var results = new List<StoreSearchResult>();

      try
      {
        using (var webClient = new WebClient { Encoding = Encoding.UTF8 })
        {
          ConfigureStoreRequest(webClient, StoreLocale);
          var searchResponse = DownloadStoreString(
            webClient,
            BuildSearchUrl(normalizedSearchTerm, StoreLocale),
            cancellationToken);
          results = ParseSearchResults(searchResponse, StoreLocale, out var searchError);
          if (!string.IsNullOrEmpty(searchError))
          {
            logger.Warn(string.Format(
              "PlayStation Store search returned an API error for {0}: {1}",
              normalizedSearchTerm,
              searchError));
          }
        }
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (TimeoutException ex)
      {
        logger.Warn(ex, "PlayStation Store search timed out for " + normalizedSearchTerm + ".");
        gameUrl = string.Empty;
        return;
      }
      catch (Exception ex)
      {
        logger.Error(ex, "Failed to search the PlayStation Store for " + normalizedSearchTerm + ".");
        gameUrl = string.Empty;
        return;
      }

      cancellationToken.ThrowIfCancellationRequested();
      if (options.IsBackgroundDownload)
      {
        SetSelectedGame(GetMatchingGame(options.GameData.Name, results));
      }
      else if (results.Count > 0)
      {
        var selectedGame = plugin.PlayniteApi.Dialogs.ChooseItemWithSearch(null, (a) =>
        {
          return GetScoredSearchResults(a, results)
            .Select(result => (GenericItemOption)result.Result)
            .ToList();
        }, options.GameData.Name, string.Empty);

        cancellationToken.ThrowIfCancellationRequested();
        SetSelectedGame(selectedGame as StoreSearchResult ??
          (selectedGame == null ? null : MatchFun(selectedGame.Name, results)));
      }
      else
      {
        gameUrl = string.Empty;
      }
    }

    private IHtmlDocument GetGamePage(CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (gamePage != null || string.IsNullOrEmpty(gameUrl))
      {
        return gamePage;
      }

      try
      {
        using (var webClient = new WebClient { Encoding = Encoding.UTF8 })
        {
          ConfigureStoreRequest(webClient, StoreLocale);
          gamePageSource = DownloadStoreString(webClient, gameUrl, cancellationToken);
          gamePage = new HtmlParser().Parse(gamePageSource);
        }
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (TimeoutException ex)
      {
        logger.Warn(ex, "PlayStation Store product page request timed out for " + gameUrl + ".");
      }
      catch (Exception ex)
      {
        logger.Warn(ex, "Failed to retrieve PlayStation Store product page " + gameUrl + ".");
      }

      return gamePage;
    }

    private StorePageMetadata GetStorePageMetadata(CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (storePageMetadata != null || string.IsNullOrEmpty(gameUrl))
      {
        return storePageMetadata;
      }

      var page = GetGamePage(cancellationToken);
      storePageMetadata = ParseStorePageMetadata(gamePageSource, page);
      return storePageMetadata;
    }

    private static string DownloadStoreString(WebClient webClient, string url, CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();

      using (var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
      {
        requestCancellation.CancelAfter(StoreRequestTimeout);
        using (requestCancellation.Token.Register(webClient.CancelAsync))
        {
          try
          {
            var response = webClient.DownloadStringTaskAsync(new Uri(url)).GetAwaiter().GetResult();
            cancellationToken.ThrowIfCancellationRequested();
            return response;
          }
          catch (Exception ex) when (cancellationToken.IsCancellationRequested)
          {
            throw new OperationCanceledException("PlayStation Store request was canceled.", ex, cancellationToken);
          }
          catch (Exception ex) when (requestCancellation.IsCancellationRequested)
          {
            throw new TimeoutException("PlayStation Store request timed out after 30 seconds.", ex);
          }
        }
      }
    }

    internal static StorePageMetadata ParseStorePageMetadata(string pageSource)
    {
      if (string.IsNullOrEmpty(pageSource))
      {
        return null;
      }

      return ParseStorePageMetadata(pageSource, new HtmlParser().Parse(pageSource));
    }

    private static StorePageMetadata ParseStorePageMetadata(string pageSource, IHtmlDocument page)
    {
      if (page == null)
      {
        return null;
      }

      var metadata = new StorePageMetadata
      {
        Description = GetDescription(page),
        BackgroundImageUrl = GetImageUrl(page,
          "img[data-qa='gameBackgroundImage#heroImage#image-no-js']",
          "img[data-qa='gameBackgroundImage#heroImage#preview']"),
        Publisher = GetText(page,
          "[data-qa='gameInfo#releaseInformation#publisher-value']",
          "[data-qa='mfe-game-title#publisher']"),
        Genres = GetText(page, "[data-qa='gameInfo#releaseInformation#genre-value']")
          ?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
          .Select(genre => genre.Trim())
          .Where(genre => !string.IsNullOrEmpty(genre))
          .ToList() ?? new List<string>(),
        ReleaseDate = GetReleaseDate(pageSource, page),
        CommunityScore = GetCommunityScore(page)
      };

      return metadata;
    }

    private static string GetDescription(IHtmlDocument page)
    {
      var description = page.QuerySelector("[data-qa='mfe-game-overview#description']")
        ?? page.QuerySelector("p.psw-c-bg-card-1");
      if (description != null)
      {
        return description.InnerHtml;
      }

      return page.QuerySelector("meta[name='description']")?.GetAttribute("content");
    }

    private static string GetText(IHtmlDocument page, params string[] selectors)
    {
      foreach (var selector in selectors)
      {
        var value = page.QuerySelector(selector)?.TextContent?.Trim();
        if (!string.IsNullOrEmpty(value))
        {
          return value;
        }
      }

      return null;
    }

    private static string GetImageUrl(IHtmlDocument page, params string[] selectors)
    {
      foreach (var selector in selectors)
      {
        var imageUrl = page.QuerySelector(selector)?.GetAttribute("src");
        if (!string.IsNullOrEmpty(imageUrl))
        {
          return imageUrl.Split('?')[0];
        }
      }

      return null;
    }

    private static ReleaseDate? GetReleaseDate(string pageSource, IHtmlDocument page)
    {
      var releaseDateMatch = Regex.Match(pageSource ?? string.Empty,
        "\"releaseDate\"\\s*:\\s*\"(?<date>\\d{4}-\\d{2}-\\d{2})");
      if (releaseDateMatch.Success &&
          DateTime.TryParseExact(releaseDateMatch.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var releaseDate))
      {
        return new ReleaseDate(releaseDate);
      }

      var displayDate = GetText(page, "[data-qa='gameInfo#releaseInformation#releaseDate-value']");
      if (DateTime.TryParse(displayDate, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.AllowWhiteSpaces,
        out releaseDate))
      {
        return new ReleaseDate(releaseDate);
      }

      return null;
    }

    private static int? GetCommunityScore(IHtmlDocument page)
    {
      var score = GetText(page, "[data-qa='mfe-game-title#average-rating']");
      if (!double.TryParse(score, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rating) ||
          rating < 0 || rating > 5)
      {
        return null;
      }

      return (int)Math.Round(rating * 20, MidpointRounding.AwayFromZero);
    }

    private static string BuildSearchUrl(string searchTerm)
    {
      return BuildSearchUrl(searchTerm, DefaultStoreLocale);
    }

    internal static string BuildSearchUrl(string searchTerm, string storeLocale)
    {
      var locale = StoreLocaleOptions.GetValidOrDefault(storeLocale).Split('-');
      var countryCode = locale[locale.Length - 1].ToUpperInvariant();
      var languageCode = locale[0].ToLowerInvariant();
      if (languageCode == "zh" && locale.Length > 2 && locale[1].Equals("hant", StringComparison.OrdinalIgnoreCase))
      {
        languageCode = "ch";
      }

      var escapedSearchTerm = searchTerm.Replace("\\", "\\\\").Replace("\"", "\\\"");
      var variables = string.Format(
        "{{\"countryCode\":\"{0}\",\"languageCode\":\"{1}\",\"nextCursor\":\"\",\"pageOffset\":0,\"pageSize\":24,\"searchTerm\":\"{2}\"}}",
        countryCode,
        languageCode,
        escapedSearchTerm);
      var extensions = string.Format("{{\"persistedQuery\":{{\"version\":1,\"sha256Hash\":\"{0}\"}}}}", SearchQueryHash);

      return string.Format(
        "{0}?operationName=getSearchResults&variables={1}&extensions={2}",
        SearchUrl,
        Uri.EscapeDataString(variables),
        Uri.EscapeDataString(extensions));
    }

    private static void ConfigureStoreRequest(WebClient webClient, string storeLocale)
    {
      webClient.Headers[HttpRequestHeader.Accept] = "application/json";
      webClient.Headers[HttpRequestHeader.ContentType] = "application/json";
      webClient.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Safari/537.36";
      webClient.Headers["Origin"] = "https://store.playstation.com";
      webClient.Headers["Referer"] = "https://store.playstation.com/";
      webClient.Headers["apollographql-client-name"] = StoreApplicationName;
      webClient.Headers["apollographql-client-version"] = StoreApplicationVersion;
      webClient.Headers["X-PSN-App-Ver"] = string.Format("{0}/{1}-", StoreApplicationName, StoreApplicationVersion);
      webClient.Headers["X-PSN-Correlation-ID"] = Guid.NewGuid().ToString();
      webClient.Headers["X-PSN-Request-ID"] = Guid.NewGuid().ToString();
      webClient.Headers["X-PSN-Store-Locale-Override"] = ToStoreLocaleHeader(storeLocale);
    }

    private static string ToStoreLocaleHeader(string storeLocale)
    {
      var locale = StoreLocaleOptions.GetValidOrDefault(storeLocale).Split('-');
      for (var index = 0; index < locale.Length; index++)
      {
        locale[index] = index == locale.Length - 1
          ? locale[index].ToUpperInvariant()
          : locale[index].ToLowerInvariant();
      }

      return string.Join("-", locale);
    }

    internal static List<StoreSearchResult> ParseSearchResults(string response)
    {
      return ParseSearchResults(response, DefaultStoreLocale, out _);
    }

    internal static List<StoreSearchResult> ParseSearchResults(string response, out string searchError)
    {
      return ParseSearchResults(response, DefaultStoreLocale, out searchError);
    }

    internal static List<StoreSearchResult> ParseSearchResults(string response, string storeLocale, out string searchError)
    {
      searchError = null;
      using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(response)))
      {
        var serializer = new DataContractJsonSerializer(typeof(PlayStationSearchResponse));
        var searchResponse = serializer.ReadObject(stream) as PlayStationSearchResponse;
        searchError = GetSearchError(searchResponse);
        var searchResults = searchResponse?.Data?.UniversalSearch?.Results ?? new List<PlayStationSearchItem>();
        var results = new List<StoreSearchResult>();

        foreach (var result in searchResults)
        {
          var coverUrl = GetMediaUrl(result.Media, "MASTER", "PORTRAIT_BANNER", "EDITION_KEY_ART", "GAMEHUB_COVER_ART") ?? GetAnyImageUrl(result.Media);
          if (string.IsNullOrEmpty(result.Name) || string.IsNullOrEmpty(coverUrl) || string.IsNullOrEmpty(result.Id))
          {
            continue;
          }

          var descriptionParts = new List<string>();
          if (!string.IsNullOrEmpty(result.LocalizedStoreDisplayClassification))
          {
            descriptionParts.Add(result.LocalizedStoreDisplayClassification);
          }

          if (result.Platforms != null && result.Platforms.Count > 0)
          {
            descriptionParts.Add(string.Join(", ", result.Platforms));
          }

          var route = string.Equals(result.Type, "Concept", StringComparison.OrdinalIgnoreCase) ? "concept" : "product";
          results.Add(new StoreSearchResult
          {
            Name = result.Name,
            Description = string.Join(" · ", descriptionParts),
            CoverUrl = coverUrl,
            BackgroundUrl = GetMediaUrl(result.Media, "BACKGROUND", "SIXTEEN_BY_NINE_BANNER"),
            StoreDisplayClassification = result.StoreDisplayClassification,
            Platforms = result.Platforms,
            GameUrl = string.Format(
              "https://store.playstation.com/{0}/{1}/{2}",
              StoreLocaleOptions.GetValidOrDefault(storeLocale),
              route,
              result.Id)
          });
        }

        return results;
      }
    }

    private static string GetSearchError(PlayStationSearchResponse searchResponse)
    {
      if (searchResponse?.Errors?.Count > 0)
      {
        return string.Join("; ", searchResponse.Errors
          .Where(error => !string.IsNullOrEmpty(error.Message))
          .Select(error => error.Message));
      }

      if (searchResponse?.Data?.UniversalSearch == null)
      {
        return "The response did not contain universal search data. The Store API may have changed.";
      }

      return null;
    }

    private static string GetMediaUrl(List<PlayStationStoreMedia> media, params string[] roles)
    {
      if (media == null)
      {
        return null;
      }

      foreach (var role in roles)
      {
        var item = media.FirstOrDefault(a =>
          string.Equals(a.Type, "IMAGE", StringComparison.OrdinalIgnoreCase) &&
          string.Equals(a.Role, role, StringComparison.OrdinalIgnoreCase) &&
          !string.IsNullOrEmpty(a.Url));
        if (item != null)
        {
          return item.Url;
        }
      }

      return null;
    }

    private static string GetAnyImageUrl(List<PlayStationStoreMedia> media)
    {
      return media?.FirstOrDefault(a =>
        string.Equals(a.Type, "IMAGE", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrEmpty(a.Url))?.Url;
    }

    private void SetSelectedGame(StoreSearchResult selectedGame)
    {
      if (selectedGame == null)
      {
        gameUrl = string.Empty;
        return;
      }

      gamePageSource = null;
      gamePage = null;
      storePageMetadata = null;

      cover = new MetadataFile(selectedGame.CoverUrl);
      if (!string.IsNullOrEmpty(selectedGame.BackgroundUrl))
      {
        background = new MetadataFile(selectedGame.BackgroundUrl);
      }

      gameUrl = selectedGame.GameUrl;
    }

    internal StoreSearchResult MatchFun(string matchName, List<StoreSearchResult> list)
    {
      var res = list.FirstOrDefault(a => string.Equals(matchName, a.Name, StringComparison.InvariantCultureIgnoreCase));
      if (res != null)
      {
        return res;
      }

      return null;
    }

    public StoreSearchResult GetMatchingGame(string gameName, List<StoreSearchResult> results)
    {
      var scoredResults = GetScoredSearchResults(gameName, results)
        .Where(result => result.Score > 0)
        .ToList();

      if (scoredResults.Count == 0)
      {
        return null;
      }

      var bestResult = scoredResults[0];
      var runnerUp = scoredResults.Skip(1).FirstOrDefault();
      if (runnerUp != null &&
          bestResult.Score < WeakMatchScore &&
          bestResult.Score - runnerUp.Score < AmbiguousMatchScoreDifference)
      {
        logger.Debug(string.Format(
          "Skipping ambiguous PSN Store match for {0}: {1} ({2}) vs {3} ({4}).",
          gameName,
          bestResult.Result.Name,
          bestResult.Score,
          runnerUp.Result.Name,
          runnerUp.Score));
        return null;
      }

      logger.Debug(string.Format(
        "Selected PSN Store match for {0}: {1} ({2}).",
        gameName,
        bestResult.Result.Name,
        bestResult.Score));
      return bestResult.Result;
    }

    private List<ScoredSearchResult> GetScoredSearchResults(string gameName, IEnumerable<StoreSearchResult> results)
    {
      return results
        .Select(result => new ScoredSearchResult(result, GetMatchScore(gameName, result)))
        .OrderByDescending(result => result.Score)
        .ThenBy(result => result.Result.Name, StringComparer.InvariantCultureIgnoreCase)
        .ToList();
    }

    internal int GetMatchScore(string gameName, StoreSearchResult result)
    {
      if (result == null || string.IsNullOrEmpty(result.Name))
      {
        return 0;
      }

      var requestedTitle = GetComparisonTitle(gameName);
      var candidateTitle = GetComparisonTitle(result.Name);
      if (string.IsNullOrEmpty(requestedTitle) || string.IsNullOrEmpty(candidateTitle))
      {
        return 0;
      }

      var requestedQualifiers = GetEditionQualifiers(requestedTitle);
      var candidateQualifiers = GetEditionQualifiers(candidateTitle);
      var exactTitleMatch = string.Equals(requestedTitle, candidateTitle, StringComparison.InvariantCultureIgnoreCase);
      var baseTitleMatch = string.Equals(
        RemoveEditionQualifiers(requestedTitle),
        RemoveEditionQualifiers(candidateTitle),
        StringComparison.InvariantCultureIgnoreCase);

      if (!exactTitleMatch && !baseTitleMatch)
      {
        return 0;
      }

      var score = exactTitleMatch ? 1000 : 700;
      var missingRequestedQualifiers = requestedQualifiers.Except(candidateQualifiers).Count();
      var extraCandidateQualifiers = candidateQualifiers.Except(requestedQualifiers).Count();
      if (requestedQualifiers.Count > 0 && missingRequestedQualifiers == 0)
      {
        score += 200;
      }

      if (missingRequestedQualifiers > 0)
      {
        score -= 130 * missingRequestedQualifiers;
      }

      if (extraCandidateQualifiers > 0)
      {
        score -= 200 * extraCandidateQualifiers;
      }

      score += GetClassificationScore(result.StoreDisplayClassification, requestedQualifiers.Count > 0);
      score += GetPlatformScore(result.Platforms);
      return score;
    }

    private static string GetComparisonTitle(string title)
    {
      var normalizedTitle = StringExtensions.NormalizeGameName(title)
        .Replace("&", " and ")
        .Replace("'", string.Empty)
        .ToLowerInvariant();
      normalizedTitle = RemoveTrailingStorePlatformLabels(normalizedTitle);
      normalizedTitle = Regex.Replace(normalizedTitle, @"\b([1-9]|[12]\d|30)\b", ReplaceSmallNumbersForRomans);
      normalizedTitle = Regex.Replace(normalizedTitle, @"[^a-z0-9]+", " ");
      normalizedTitle = Regex.Replace(normalizedTitle, @"\s+", " ").Trim();
      return Regex.Replace(normalizedTitle, @"^the\s+", string.Empty);
    }

    private static string RemoveTrailingStorePlatformLabels(string title)
    {
      const string platformLabel = @"(?:ps\s*[345]|playstation\s*[345]|ps\s*vr(?:\s*2)?|playstation\s*vr(?:\s*2)?)";
      return Regex.Replace(
        title,
        @"(?:\s*(?:and|&|,|/|-)?\s*" + platformLabel + @")+\s*$",
        string.Empty,
        RegexOptions.IgnoreCase).Trim();
    }

    private static string ReplaceSmallNumbersForRomans(Match match)
    {
      return Roman.To(int.Parse(match.Value)).ToLowerInvariant();
    }

    private static HashSet<string> GetEditionQualifiers(string title)
    {
      var qualifiers = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
      foreach (var qualifier in EditionQualifierAliases)
      {
        if (Regex.IsMatch(title, @"\b" + Regex.Escape(qualifier.Key) + @"\b", RegexOptions.IgnoreCase))
        {
          qualifiers.Add(qualifier.Value);
        }
      }

      return qualifiers;
    }

    private static string RemoveEditionQualifiers(string title)
    {
      var baseTitle = title;
      foreach (var qualifier in EditionQualifierAliases.Keys)
      {
        baseTitle = Regex.Replace(baseTitle, @"\b" + Regex.Escape(qualifier) + @"\b", " ", RegexOptions.IgnoreCase);
      }

      baseTitle = Regex.Replace(baseTitle, @"\bedition\b", " ", RegexOptions.IgnoreCase);
      return Regex.Replace(baseTitle, @"\s+", " ").Trim();
    }

    private static int GetClassificationScore(string classification, bool requestedEdition)
    {
      switch (classification)
      {
        case "FULL_GAME":
          return 120;
        case "GAME_BUNDLE":
          return -120;
        case "PREMIUM_EDITION":
          return requestedEdition ? 40 : -160;
        case "ADD_ON":
        case "ADD_ON_PACK":
        case "CHARACTER":
        case "COSTUME":
        case "GAME_LEVEL":
        case "ITEM":
        case "VIRTUAL_CURRENCY":
        case "DEMO":
          return -500;
        default:
          return 0;
      }
    }

    private int GetPlatformScore(List<string> resultPlatforms)
    {
      var gamePlatforms = options?.GameData?.Platforms?
        .Select(platform => GetPlayStationPlatform(platform.Name))
        .Where(platform => !string.IsNullOrEmpty(platform))
        .Distinct(StringComparer.InvariantCultureIgnoreCase)
        .ToList();
      if (gamePlatforms == null || gamePlatforms.Count == 0 || resultPlatforms == null || resultPlatforms.Count == 0)
      {
        return 0;
      }

      return resultPlatforms.Any(platform => gamePlatforms.Contains(platform, StringComparer.InvariantCultureIgnoreCase)) ? 60 : -40;
    }

    private static string GetPlayStationPlatform(string platformName)
    {
      if (string.IsNullOrEmpty(platformName))
      {
        return null;
      }

      if (platformName.IndexOf("PlayStation 5", StringComparison.InvariantCultureIgnoreCase) >= 0 ||
          string.Equals(platformName, "PS5", StringComparison.InvariantCultureIgnoreCase))
      {
        return "PS5";
      }

      if (platformName.IndexOf("PlayStation 4", StringComparison.InvariantCultureIgnoreCase) >= 0 ||
          string.Equals(platformName, "PS4", StringComparison.InvariantCultureIgnoreCase))
      {
        return "PS4";
      }

      if (platformName.IndexOf("PlayStation VR", StringComparison.InvariantCultureIgnoreCase) >= 0 ||
          string.Equals(platformName, "PS VR", StringComparison.InvariantCultureIgnoreCase))
      {
        return "PS VR";
      }

      if (platformName.IndexOf("PlayStation 3", StringComparison.InvariantCultureIgnoreCase) >= 0 ||
          string.Equals(platformName, "PS3", StringComparison.InvariantCultureIgnoreCase))
      {
        return "PS3";
      }

      if (platformName.IndexOf("PlayStation Vita", StringComparison.InvariantCultureIgnoreCase) >= 0 ||
          string.Equals(platformName, "PS Vita", StringComparison.InvariantCultureIgnoreCase))
      {
        return "PS Vita";
      }

      if (platformName.IndexOf("PlayStation Portable", StringComparison.InvariantCultureIgnoreCase) >= 0 ||
          string.Equals(platformName, "PSP", StringComparison.InvariantCultureIgnoreCase))
      {
        return "PSP";
      }

      return null;
    }

    private class ScoredSearchResult
    {
      public StoreSearchResult Result { get; }
      public int Score { get; }

      public ScoredSearchResult(StoreSearchResult result, int score)
      {
        Result = result;
        Score = score;
      }
    }
  }

  [DataContract]
  internal class PlayStationSearchResponse
  {
    [DataMember(Name = "data")]
    public PlayStationSearchData Data { get; set; }

    [DataMember(Name = "errors")]
    public List<PlayStationSearchError> Errors { get; set; }
  }

  [DataContract]
  internal class PlayStationSearchError
  {
    [DataMember(Name = "message")]
    public string Message { get; set; }
  }

  [DataContract]
  internal class PlayStationSearchData
  {
    [DataMember(Name = "universalSearch")]
    public PlayStationSearchDataPage UniversalSearch { get; set; }
  }

  [DataContract]
  internal class PlayStationSearchDataPage
  {
    [DataMember(Name = "results")]
    public List<PlayStationSearchItem> Results { get; set; }
  }

  [DataContract]
  internal class PlayStationSearchItem
  {
    [DataMember(Name = "__typename")]
    public string Type { get; set; }

    [DataMember(Name = "id")]
    public string Id { get; set; }

    [DataMember(Name = "name")]
    public string Name { get; set; }

    [DataMember(Name = "localizedStoreDisplayClassification")]
    public string LocalizedStoreDisplayClassification { get; set; }

    [DataMember(Name = "storeDisplayClassification")]
    public string StoreDisplayClassification { get; set; }

    [DataMember(Name = "platforms")]
    public List<string> Platforms { get; set; }

    [DataMember(Name = "media")]
    public List<PlayStationStoreMedia> Media { get; set; }
  }

  [DataContract]
  internal class PlayStationStoreMedia
  {
    [DataMember(Name = "role")]
    public string Role { get; set; }

    [DataMember(Name = "type")]
    public string Type { get; set; }

    [DataMember(Name = "url")]
    public string Url { get; set; }
  }


}
