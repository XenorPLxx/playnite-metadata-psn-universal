using AngleSharp.Dom.Html;
using AngleSharp.Parser.Html;
using Playnite.Common;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
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
    private IHtmlDocument gamePage;
    private const string SearchUrl = "https://web.np.playstation.com/api/graphql/v1//op";
    private const string SearchQueryHash = "4df6284f982e57bec70f23c77e2c219dc792eb19af7fb3d3a81767aa3f1958aa";
    private const string StoreLocale = "en-us";
    private const string StoreApplicationName = "@sie-ppr-web-store/app";
    private const string StoreApplicationVersion = "0.113.0";
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
            //MetadataField.CommunityScore,
            MetadataField.CoverImage,
            //MetadataField.CriticScore,
            //MetadataField.Developers,
            //MetadataField.Genres,
            MetadataField.Icon,
            //MetadataField.Links,
            //MetadataField.Publishers,
            //MetadataField.ReleaseDate,
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

    // Override additional methods based on supported metadata fields.
    //public override string GetDescription(GetMetadataFieldArgs args)
    //{
    //    return options.GameData.Name + " description";
    //}

    public override MetadataFile GetCoverImage(GetMetadataFieldArgs args)
    {
      GetSearchResults(options.GameData.Name);
      if (cover != null)
      {
        return cover;
      }
      return base.GetCoverImage(args);
    }

    // Covers are square, so might be useful as icons
    public override MetadataFile GetIcon(GetMetadataFieldArgs args)
    {
      GetSearchResults(options.GameData.Name);
      if (cover != null)
      {
        return cover;
      }
      return base.GetIcon(args);
    }

    public override MetadataFile GetBackgroundImage(GetMetadataFieldArgs args)
    {
      GetSearchResults(options.GameData.Name);
      if (background != null)
      {
        return background;
      }

      if (gameUrl != null && gameUrl != "")
      {
        var page = GetGamePage();
        var backgroundImageUrlTag = page?.QuerySelector(".psw-l-fit-cover");
        var backgroundImageUrl = backgroundImageUrlTag?.GetAttribute("src");
        if (!string.IsNullOrEmpty(backgroundImageUrl))
        {
          return new MetadataFile(backgroundImageUrl.Split('?')[0]);
        }
      }
      return base.GetBackgroundImage(args);
    }


    public override string GetDescription(GetMetadataFieldArgs args)
    {
      GetSearchResults(options.GameData.Name);
      if (gameUrl != null && gameUrl != "")
      {
        var page = GetGamePage();
        var descriptionTag = page?.QuerySelector("p.psw-c-bg-card-1");
        if (descriptionTag != null)
        {
          return descriptionTag.InnerHtml;
        }

        var descriptionMetaTag = page?.QuerySelector("meta[name='description']");
        var description = descriptionMetaTag?.GetAttribute("content");
        if (!string.IsNullOrEmpty(description))
        {
          return description;
        }
      }
      return base.GetDescription(args);
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

    public void GetSearchResults(string searchTerm)
    {
      if (gameUrl != null) { return; }
      var normalizedSearchTerm = StringExtensions.NormalizeGameName(searchTerm);
      var results = new List<StoreSearchResult>();

      try
      {
        using (var webClient = new WebClient { Encoding = Encoding.UTF8 })
        {
          ConfigureStoreRequest(webClient);
          var searchResponse = webClient.DownloadString(BuildSearchUrl(normalizedSearchTerm));
          results = ParseSearchResults(searchResponse);
        }
      }
      catch (Exception ex)
      {
        logger.Error(ex, "Failed to search the PlayStation Store for " + normalizedSearchTerm + ".");
        gameUrl = string.Empty;
        return;
      }

      if (options.IsBackgroundDownload)
      {
        SetSelectedGame(GetMatchingGame(options.GameData.Name, results));
      }
      else if (results.Count > 0)
      {
        var selectedGame = plugin.PlayniteApi.Dialogs.ChooseItemWithSearch(null, (a) =>
        {
          return new List<GenericItemOption>(results);
        }, options.GameData.Name, string.Empty);

        SetSelectedGame(selectedGame == null ? null : MatchFun(selectedGame.Name, results));
      }
      else
      {
        gameUrl = string.Empty;
      }
    }

    private IHtmlDocument GetGamePage()
    {
      if (gamePage != null || string.IsNullOrEmpty(gameUrl))
      {
        return gamePage;
      }

      try
      {
        using (var webClient = new WebClient { Encoding = Encoding.UTF8 })
        {
          var parser = new HtmlParser();
          gamePage = parser.Parse(webClient.DownloadString(gameUrl));
        }
      }
      catch (Exception ex)
      {
        logger.Warn(ex, "Failed to retrieve PlayStation Store product page " + gameUrl + ".");
      }

      return gamePage;
    }

    private static string BuildSearchUrl(string searchTerm)
    {
      var escapedSearchTerm = searchTerm.Replace("\\", "\\\\").Replace("\"", "\\\"");
      var variables = string.Format(
        "{{\"countryCode\":\"US\",\"languageCode\":\"en\",\"nextCursor\":\"\",\"pageOffset\":0,\"pageSize\":24,\"searchTerm\":\"{0}\"}}",
        escapedSearchTerm);
      var extensions = string.Format("{{\"persistedQuery\":{{\"version\":1,\"sha256Hash\":\"{0}\"}}}}", SearchQueryHash);

      return string.Format(
        "{0}?operationName=getSearchResults&variables={1}&extensions={2}",
        SearchUrl,
        Uri.EscapeDataString(variables),
        Uri.EscapeDataString(extensions));
    }

    private static void ConfigureStoreRequest(WebClient webClient)
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
      webClient.Headers["X-PSN-Store-Locale-Override"] = "en-US";
    }

    internal static List<StoreSearchResult> ParseSearchResults(string response)
    {
      using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(response)))
      {
        var serializer = new DataContractJsonSerializer(typeof(PlayStationSearchResponse));
        var searchResponse = serializer.ReadObject(stream) as PlayStationSearchResponse;
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
            GameUrl = string.Format("https://store.playstation.com/{0}/{1}/{2}", StoreLocale, route, result.Id)
          });
        }

        return results;
      }
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
      var scoredResults = results
        .Select(result => new ScoredSearchResult(result, GetMatchScore(gameName, result)))
        .Where(result => result.Score > 0)
        .OrderByDescending(result => result.Score)
        .ThenBy(result => result.Result.Name, StringComparer.InvariantCultureIgnoreCase)
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

      if (requestedQualifiers.Count > 0 && !requestedQualifiers.All(candidateQualifiers.Contains))
      {
        return 0;
      }

      if (requestedQualifiers.Count > 0 && candidateQualifiers.Except(requestedQualifiers).Any())
      {
        return 0;
      }

      var score = exactTitleMatch ? 1000 : 700;
      if (requestedQualifiers.Count > 0)
      {
        score += 200;
      }
      else if (candidateQualifiers.Count > 0)
      {
        score -= 200;
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
      normalizedTitle = Regex.Replace(normalizedTitle, @"\b([1-9]|[12]\d|30)\b", ReplaceSmallNumbersForRomans);
      normalizedTitle = Regex.Replace(normalizedTitle, @"[^a-z0-9]+", " ");
      normalizedTitle = Regex.Replace(normalizedTitle, @"\s+", " ").Trim();
      return Regex.Replace(normalizedTitle, @"^the\s+", string.Empty);
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
          return 80;
        case "PREMIUM_EDITION":
          return requestedEdition ? 60 : -80;
        case "ADD_ON":
        case "CHARACTER":
        case "ITEM":
        case "DEMO":
          return -250;
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
