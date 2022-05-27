using AngleSharp.Parser.Html;
using Playnite.Common;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace UniversalPSNMetadata
{
    public class UniversalPSNMetadataProvider : OnDemandMetadataProvider
    {
        private readonly MetadataRequestOptions options;
        private readonly UniversalPSNMetadata plugin;
        private MetadataFile cover;
        private const string searchUrl = @"https://store.playstation.com/search/{0}";

        public override List<MetadataField> AvailableFields { get; } = new List<MetadataField>
        {
            //MetadataField.Description,
            //MetadataField.BackgroundImage,
            //MetadataField.CommunityScore,
            MetadataField.CoverImage,
            //MetadataField.CriticScore,
            //MetadataField.Developers,
            //MetadataField.Genres,
            //MetadataField.Icon,
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

        internal void GetGameData()
        {

        }

        public class StoreSearchResult
        {
            public string Title { get; set; }
            public string CoverUrl { get; set; }

        }



        public void GetSearchResults(string searchTerm)
        {
            using (var webClient = new WebClient { Encoding = Encoding.UTF8 })
            {

                var normalizedSearchTerm = StringExtensions.NormalizeGameName(searchTerm);
                var searchPageSrc = webClient.DownloadString(string.Format(searchUrl, normalizedSearchTerm));
                var parser = new HtmlParser();
                var searchPage = parser.Parse(searchPageSrc);
                var results = new List<StoreSearchResult>();
                foreach (var gameElem in searchPage.QuerySelectorAll(".psw-grid-list li"))
                {
                    var title = gameElem.QuerySelector(".psw-t-body").InnerHtml;                    
                    var coverUrl = gameElem.QuerySelector(".psw-l-fit-cover").GetAttribute("src").Split('?')[0];
                    results.Add(new StoreSearchResult
                    {
                        Title = HttpUtility.HtmlDecode(title),
                        CoverUrl = HttpUtility.HtmlDecode(coverUrl)
                    });

                    //cover = new MetadataFile(coverString);


                    //var gameId = gameElem.GetAttribute("psw-t-body");
                    //results.Add(new StoreSearchResult
                    //{
                    //    Name = HttpUtility.HtmlDecode(title),
                    //    Description = HttpUtility.HtmlDecode(releaseDate),
                    //    GameId = uint.Parse(gameId)
                    //});
                }
                var matchedGame = GetMatchingGame(normalizedSearchTerm, results);
                if (matchedGame != null)
                {
                    cover = new MetadataFile(matchedGame.CoverUrl);
                }
            }


            return;
        }

        internal StoreSearchResult MatchFun(string matchName, List<StoreSearchResult> list)
        {
            var res = list.FirstOrDefault(a => string.Equals(matchName, a.Title, StringComparison.InvariantCultureIgnoreCase));
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

        public StoreSearchResult GetMatchingGame(string normalizedSearchTerm, List<StoreSearchResult>  results)
        {
            var normalizedName = normalizedSearchTerm;
            results.ForEach(a => a.Title = StringExtensions.NormalizeGameName(a.Title));

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
            resCopy.ForEach(a => a.Title = a.Title.Replace("'", ""));
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
                res.Title = Regex.Replace(res.Title, @"\s*(:|-)\s*", " ");
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
                if (!string.IsNullOrEmpty(a.Title) && a.Title.Contains(":"))
                {
                    return string.Equals(normalizedName, a.Title.Split(':')[0], StringComparison.InvariantCultureIgnoreCase);
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


}