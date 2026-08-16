using System.Text;
using System.IO;
using UniversalPSNMetadata;

var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
string Fixture(string name) => File.ReadAllText(Path.Combine(fixtures, name));
void Equal<T>(T expected, T actual, string message) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{message} Expected: '{expected}'. Actual: '{actual}'.");
    }
}

void Contains(string value, string expected, string message)
{
    if (!value.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{message} Expected to find: '{expected}'.");
    }
}

var results = UniversalPSNMetadataGameSession.ParseSearchResults(Fixture("search-results-wukong.json"), "en-us", out var searchError);
Equal(2, results.Count, "Unexpected number of parsed search results.");
if (searchError is not null) throw new InvalidOperationException("A valid search response reported an error.");
Equal("Black Myth: Wukong", results[0].Name!, "The base game name was not retained.");
Equal("https://example.test/wukong-cover.jpg", results[0].CoverUrl!, "The master cover was not selected.");
Equal("https://example.test/wukong-background.jpg", results[0].BackgroundUrl!, "The background image was not selected.");
Equal("HP6545-PPSA23226_00-GAME000000000000", results[0].StoreId!, "The PlayStation Store id was not retained.");
Equal("https://store.playstation.com/pl-pl/product/HP6545-PPSA23226_00-GAME000000000000", UniversalPSNMetadataGameSession.ParseSearchResults(Fixture("search-results-wukong.json"), "pl-pl", out _)[0].GameUrl!, "Search results did not use the configured Store locale.");

var polishUrl = Uri.UnescapeDataString(UniversalPSNMetadataGameSession.BuildSearchUrl("wukong", "pl-pl"));
Contains(polishUrl, "\"countryCode\":\"PL\"", "Polish Store searches did not use the selected country.");
Contains(polishUrl, "\"languageCode\":\"pl\"", "Polish Store searches did not use the selected language.");
var chineseUrl = Uri.UnescapeDataString(UniversalPSNMetadataGameSession.BuildSearchUrl("wukong", "zh-hant-tw"));
Contains(chineseUrl, "\"countryCode\":\"TW\"", "Taiwan Store searches did not use the selected country.");
Contains(chineseUrl, "\"languageCode\":\"ch\"", "Traditional Chinese Store searches did not use the Store API language code.");

var apiError = UniversalPSNMetadataGameSession.ParseSearchResults(Fixture("search-response-error.json"), "en-us", out var apiErrorMessage);
Equal(0, apiError.Count, "An API error response returned games.");
Equal("PersistedQueryNotFound", apiErrorMessage!, "The Store API error was not exposed.");

StoreSearchResult Result(string name, string classification) => new(name, null) { StoreDisplayClassification = classification, Platforms = ["PS5"] };
var match = UniversalPSNMetadataGameSession.GetMatchingGame("Black Myth: Wukong", results);
Equal("Black Myth: Wukong", match!.Name!, "The base game did not outrank the deluxe edition.");
match = UniversalPSNMetadataGameSession.GetMatchingGame("Final Fantasy 7 Remake", [Result("Final Fantasy VII", "FULL_GAME"), Result("Final Fantasy VII Remake", "FULL_GAME")]);
Equal("Final Fantasy VII Remake", match!.Name!, "The remake did not outrank the original game.");
match = UniversalPSNMetadataGameSession.GetMatchingGame("Like a Dragon: Pirate Yakuza in Hawaii", [Result("Like a Dragon: Pirate Yakuza in Hawaii Demo PS5", "DEMO"), Result("Like a Dragon: Pirate Yakuza in Hawaii PS4 & PS5", "FULL_GAME")]);
Equal("Like a Dragon: Pirate Yakuza in Hawaii PS4 & PS5", match!.Name!, "Trailing PS4/PS5 labels prevented the full game from matching.");
match = UniversalPSNMetadataGameSession.GetMatchingGame("Mass Effect 2 (2010) Edition", [Result("Mass Effect Legendary Edition", "FULL_GAME"), Result("Mass Effect: Andromeda", "FULL_GAME")]);
if (match is not null) throw new InvalidOperationException("A different Mass Effect title was selected as an automatic match.");
match = UniversalPSNMetadataGameSession.GetMatchingGame("Tomb Raider IV-VI Remastered", [Result("Tomb Raider IV-VI Remastered PS4 & PS5", "FULL_GAME")]);
Equal("Tomb Raider IV-VI Remastered PS4 & PS5", match!.Name!, "Trailing platform labels prevented a Roman-number title from matching.");
match = UniversalPSNMetadataGameSession.GetMatchingGame("Harry Potter: Quidditch Champions", [Result("Harry Potter: Quidditch Champions Deluxe Edition PS4 & PS5", "GAME_BUNDLE"), Result("Harry Potter: Quidditch Champions PS4 & PS5", "FULL_GAME")]);
Equal("Harry Potter: Quidditch Champions PS4 & PS5", match!.Name!, "A game bundle outranked the matching full game.");
match = UniversalPSNMetadataGameSession.GetMatchingGame("Grand Theft Auto V Enhanced", [Result("Grand Theft Auto V (PS4\u2122 & PS5\u2122)", "GAME_BUNDLE")]);
Equal("Grand Theft Auto V (PS4\u2122 & PS5\u2122)", match!.Name!, "A missing Store edition suffix discarded an otherwise strong game match.");
match = UniversalPSNMetadataGameSession.GetMatchingGame("I Expect You To Die 3", [Result("I Expect You To Die", "FULL_GAME"), Result("I Expect You To Die 2", "FULL_GAME")]);
if (match is not null) throw new InvalidOperationException("A different I Expect You To Die sequel was selected as an automatic match.");
match = UniversalPSNMetadataGameSession.GetMatchingGame("Station to Station", [Result("Train Station Project", "FULL_GAME"), Result("Gas Station Simulator", "FULL_GAME")]);
if (match is not null) throw new InvalidOperationException("An unrelated station game was selected as an automatic match.");

// Long and short forms of the same edition qualifier must be treated as one qualifier.
match = UniversalPSNMetadataGameSession.GetMatchingGame("Nioh 2 Remastered", [Result("Nioh 2 Remaster", "FULL_GAME")]);
Equal("Nioh 2 Remaster", match!.Name!, "A remastered/remaster spelling difference discarded the match.");
match = UniversalPSNMetadataGameSession.GetMatchingGame("Some Game Deluxe Edition", [Result("Some Game Deluxe", "FULL_GAME")]);
Equal("Some Game Deluxe", match!.Name!, "A deluxe edition/deluxe spelling difference discarded the match.");

// A candidate sold only for another platform must not outrank one that matches.
match = UniversalPSNMetadataGameSession.GetMatchingGame(
    "Some Game",
    [new StoreSearchResult("Some Game", null) { StoreDisplayClassification = "FULL_GAME", Platforms = ["PS5"] },
     new StoreSearchResult("Some Game", null) { StoreDisplayClassification = "FULL_GAME", Platforms = ["PS3"] }],
    ["PlayStation 3"]);
Equal("PS3", match!.Platforms[0], "Platform scoring did not prefer the candidate sold for the game's platform.");

// The custom locale override must reach the Store request.
var overrideUrl = Uri.UnescapeDataString(UniversalPSNMetadataGameSession.BuildSearchUrl("wukong", "en-kz"));
Contains(overrideUrl, "\"countryCode\":\"KZ\"", "A custom locale did not use its country code.");
Contains(overrideUrl, "\"languageCode\":\"en\"", "A custom locale did not use its language code.");

var page = UniversalPSNMetadataGameSession.ParseStorePageMetadata(Fixture("product-page-wukong.html"))!;
Equal("Game Science Interactive Technology Co., Ltd.", page.Publisher!, "The product page publisher was not parsed.");
Equal(90, page.CommunityScore!.Value, "The Store star rating was not converted to a 0-100 community score.");
Equal(3, page.Genres.Count, "The product page genres were not parsed.");
Equal("Role Playing Games", page.Genres[0], "The first genre was not retained.");
Equal(2024, page.ReleaseDate!.Year, "The ISO release date was not parsed.");
Equal(8, page.ReleaseDate!.Month!.Value, "The ISO release month was not parsed.");
Equal(20, page.ReleaseDate!.Day!.Value, "The ISO release day was not parsed.");
Equal("https://example.test/wukong-hero.jpg", page.BackgroundImageUrl!, "The hero background image was not selected.");

// A candidate carrying every requested qualifier must beat one carrying none, even when it also
// carries extra qualifiers of its own.
match = UniversalPSNMetadataGameSession.GetMatchingGame("Some Game Remastered", [Result("Some Game", "FULL_GAME"), Result("Some Game Remastered Deluxe Edition", "FULL_GAME")]);
Equal("Some Game Remastered Deluxe Edition", match!.Name!, "A plain title outranked the edition holding the requested qualifier.");

// A blank url on the first image must not discard the result.
const string blankFirstImage = """
{"data":{"universalSearch":{"results":[{
  "id":"TEST-3","name":"Blank First Image","__typename":"Product","storeDisplayClassification":"FULL_GAME",
  "media":[{"type":"IMAGE","role":"SCREENSHOT","url":""},{"type":"IMAGE","role":"SCREENSHOT","url":"https://example.test/shot.jpg"}]}]}}}
""";
var blank = UniversalPSNMetadataGameSession.ParseSearchResults(blankFirstImage, "en-us", out _);
Equal(1, blank.Count, "A result whose first image url was blank was discarded.");

// Real Store responses put MASTER last in the media array. Selecting by array order rather than by
// role preference silently picked the wrong aspect ratio on most results.
const string mediaOrderResponse = """
{"data":{"universalSearch":{"results":[{
  "id":"TEST-1","name":"Media Order Test","__typename":"Product","storeDisplayClassification":"FULL_GAME",
  "media":[
    {"type":"IMAGE","role":"FOUR_BY_THREE_BANNER","url":"https://example.test/four-by-three.jpg"},
    {"type":"IMAGE","role":"GAMEHUB_COVER_ART","url":"https://example.test/gamehub.jpg"},
    {"type":"IMAGE","role":"SIXTEEN_BY_NINE_BANNER","url":"https://example.test/sixteen-by-nine.jpg"},
    {"type":"IMAGE","role":"BACKGROUND","url":"https://example.test/background.jpg"},
    {"type":"IMAGE","role":"MASTER","url":"https://example.test/master.jpg"}]}]}}}
""";
var ordered = UniversalPSNMetadataGameSession.ParseSearchResults(mediaOrderResponse, "en-us", out _);
Equal(1, ordered.Count, "The media-order response did not parse.");
Equal("https://example.test/master.jpg", ordered[0].CoverUrl!, "The cover ignored role preference and took the first media entry.");
Equal("https://example.test/background.jpg", ordered[0].BackgroundUrl!, "The background ignored role preference.");

// A preferred role present but with no usable url must fall through to the next role.
const string emptyUrlResponse = """
{"data":{"universalSearch":{"results":[{
  "id":"TEST-2","name":"Empty Url Test","__typename":"Product","storeDisplayClassification":"FULL_GAME",
  "media":[
    {"type":"IMAGE","role":"MASTER","url":""},
    {"type":"IMAGE","role":"PORTRAIT_BANNER","url":"https://example.test/portrait.jpg"}]}]}}}
""";
var emptyUrl = UniversalPSNMetadataGameSession.ParseSearchResults(emptyUrlResponse, "en-us", out _);
Equal("https://example.test/portrait.jpg", emptyUrl[0].CoverUrl!, "An empty preferred-role url was not skipped.");

// An empty errors array is not an error, and an empty result list is a legitimate outcome.
const string emptyErrors = """{"errors":[],"data":{"universalSearch":{"results":[]}}}""";
var emptyResults = UniversalPSNMetadataGameSession.ParseSearchResults(emptyErrors, "en-us", out var emptyErrorMessage);
Equal(0, emptyResults.Count, "An empty result list did not parse.");
if (emptyErrorMessage is not null) throw new InvalidOperationException("An empty errors array was reported as an error.");

// A response whose shape changed must report the fallback diagnostic rather than failing silently.
const string missingData = """{"errors":[],"data":{}}""";
UniversalPSNMetadataGameSession.ParseSearchResults(missingData, "en-us", out var missingDataMessage);
if (string.IsNullOrWhiteSpace(missingDataMessage)) throw new InvalidOperationException("A missing-results response did not report the fallback diagnostic.");

Console.WriteLine("PSN Store parser and matcher fixture checks passed.");
if (args.Contains("--live"))
{
    Console.WriteLine("Live Store check:");
    await LiveProbe.RunAsync("Epic Mickey: Rebrushed", "en-us");
    await LiveProbe.RunAsync("Black Myth: Wukong", "en-us");
}
