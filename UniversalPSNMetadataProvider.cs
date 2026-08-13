using AngleSharp.Dom.Html;
using AngleSharp.Parser.Html;
using Playnite.Common;
using Playnite.SDK;
using Playnite.SDK.Data;
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
        SetSelectedGame(GetMatchingGame(normalizedSearchTerm, results));
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

    internal string ReplaceNumsForRomans(Match m)
    {
      return Roman.To(int.Parse(m.Value));
    }

    public StoreSearchResult GetMatchingGame(string normalizedSearchTerm, List<StoreSearchResult> results)
    {
      var normalizedName = normalizedSearchTerm;
      results.ForEach(a => a.Name = StringExtensions.NormalizeGameName(a.Name));

      string testName = string.Empty;
      StoreSearchResult matchedGame = null;

      // Direct comparison
      matchedGame = MatchFun(normalizedName, results);
      if (matchedGame != null)
      {
        return matchedGame;
      }

      // Try replacing roman numerals: 3 => III
      testName = Regex.Replace(normalizedName, @"\d+", ReplaceNumsForRomans);
      matchedGame = MatchFun(testName, results);
      if (matchedGame != null)
      {
        return matchedGame;
      }

      // Try adding The
      testName = "The " + normalizedName;
      matchedGame = MatchFun(testName, results);
      if (matchedGame != null)
      {
        return matchedGame;
      }

      // Try chaning & / and
      testName = Regex.Replace(normalizedName, @"\s+and\s+", " & ", RegexOptions.IgnoreCase);
      matchedGame = MatchFun(testName, results);
      if (matchedGame != null)
      {
        return matchedGame;
      }

      // Try removing apostrophes
      var resCopy = Serialization.GetClone(results);
      resCopy.ForEach(a => a.Name = a.Name.Replace("'", ""));
      matchedGame = MatchFun(normalizedName, resCopy);
      if (matchedGame != null)
      {
        return matchedGame;
      }

      // Try removing all ":" and "-"
      testName = Regex.Replace(normalizedName, @"\s*(:|-)\s*", " ");
      resCopy = Serialization.GetClone(results);
      foreach (var res in resCopy)
      {
        res.Name = Regex.Replace(res.Name, @"\s*(:|-)\s*", " ");
      }

      matchedGame = MatchFun(testName, resCopy);
      if (matchedGame != null)
      {
        return matchedGame;
      }

      // Try adding 'PS4 & PS5'
      testName = normalizedName + " PS4 & PS5";
      resCopy = Serialization.GetClone(results);
      matchedGame = MatchFun(testName, resCopy);
      if (matchedGame != null)
      {
        return matchedGame;
      }

      // Try without subtitle
      var testResult = results.FirstOrDefault(a =>
      {
        if (!string.IsNullOrEmpty(a.Name) && a.Name.Contains(":"))
        {
          return string.Equals(normalizedName, a.Name.Split(':')[0], StringComparison.InvariantCultureIgnoreCase);
        }

        return false;
      });

      if (testResult != null)
      {
        return testResult;
      }

      return null;
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
