$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$pluginAssembly = Join-Path $repo 'bin\Release\UniversalPSNMetadata.dll'
$sdkAssembly = Join-Path $repo 'packages\PlayniteSDK.6.12.0\lib\net462\Playnite.SDK.dll'

if (-not (Test-Path $pluginAssembly)) {
    throw 'Build the Release configuration before running fixture checks.'
}

[System.Reflection.Assembly]::LoadFrom($sdkAssembly) | Out-Null
$assembly = [System.Reflection.Assembly]::LoadFrom($pluginAssembly)
$providerType = $assembly.GetType('UniversalPSNMetadata.UniversalPSNMetadataProvider')
$resultType = $assembly.GetType('UniversalPSNMetadata.UniversalPSNMetadataProvider+StoreSearchResult')
$pageMetadataType = $assembly.GetType('UniversalPSNMetadata.UniversalPSNMetadataProvider+StorePageMetadata')
$parseMethod = $providerType.GetMethod('ParseSearchResults', [System.Reflection.BindingFlags]'Static, NonPublic', $null, [Type[]]@([string], [string].MakeByRefType()), $null)
$parseLocalizedMethod = $providerType.GetMethod('ParseSearchResults', [System.Reflection.BindingFlags]'Static, NonPublic', $null, [Type[]]@([string], [string], [string].MakeByRefType()), $null)
$parsePageMethod = $providerType.GetMethod('ParseStorePageMetadata', [System.Reflection.BindingFlags]'Static, NonPublic', $null, [Type[]]@([string]), $null)
$buildSearchUrlMethod = $providerType.GetMethod('BuildSearchUrl', [System.Reflection.BindingFlags]'Static, NonPublic', $null, [Type[]]@([string], [string]), $null)

function Read-Fixture($name) {
    return [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot "Fixtures\$name"))
}

function Parse-SearchResponse($response) {
    $arguments = [object[]]@($response, $null)
    $results = $parseMethod.Invoke($null, $arguments)
    return @{ Results = $results; Error = $arguments[1] }
}

function Assert-Equal($expected, $actual, $message) {
    if ($expected -ne $actual) {
        throw "$message Expected: '$expected'. Actual: '$actual'."
    }
}

function Assert-Contains($text, $expected, $message) {
    if (-not $text.Contains($expected)) {
        throw "$message Expected to find: '$expected'."
    }
}

function Assert-Null($actual, $message) {
    if ($null -ne $actual) {
        throw "$message Expected: null. Actual: '$($actual.Name)'."
    }
}

function New-StoreResult($name, $classification, [string[]]$platforms = @('PS5')) {
    $result = [Activator]::CreateInstance($resultType)
    $result.Name = $name
    $result.StoreDisplayClassification = $classification
    $result.Platforms = [System.Collections.Generic.List[string]]::new($platforms)
    return $result
}

function New-StoreResultList([object[]]$items) {
    $results = [System.Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($resultType))
    foreach ($item in $items) {
        $results.Add($item)
    }

    return $results
}

$search = Parse-SearchResponse (Read-Fixture 'search-results-wukong.json')
Assert-Equal 2 $search.Results.Count 'Unexpected number of parsed search results.'
Assert-Equal $null $search.Error 'A valid search response reported an error.'
Assert-Equal 'Black Myth: Wukong' $search.Results[0].Name 'The base game name was not retained.'
Assert-Equal 'https://example.test/wukong-cover.jpg' $search.Results[0].CoverUrl 'The master cover was not selected.'
Assert-Equal 'https://example.test/wukong-background.jpg' $search.Results[0].BackgroundUrl 'The background image was not selected.'

$localizedArguments = [object[]]@((Read-Fixture 'search-results-wukong.json'), 'pl-pl', $null)
$localizedSearch = $parseLocalizedMethod.Invoke($null, $localizedArguments)
Assert-Equal 'https://store.playstation.com/pl-pl/product/HP6545-PPSA23226_00-GAME000000000000' $localizedSearch[0].GameUrl 'Search results did not use the configured Store locale.'

$polishSearchUrl = [Uri]::UnescapeDataString($buildSearchUrlMethod.Invoke($null, @('wukong', 'pl-pl')))
Assert-Contains $polishSearchUrl '"countryCode":"PL"' 'Polish Store searches did not use the selected country.'
Assert-Contains $polishSearchUrl '"languageCode":"pl"' 'Polish Store searches did not use the selected language.'

$traditionalChineseSearchUrl = [Uri]::UnescapeDataString($buildSearchUrlMethod.Invoke($null, @('wukong', 'zh-hant-tw')))
Assert-Contains $traditionalChineseSearchUrl '"countryCode":"TW"' 'Taiwan Store searches did not use the selected country.'
Assert-Contains $traditionalChineseSearchUrl '"languageCode":"ch"' 'Traditional Chinese Store searches did not use the Store API language code.'

$customSearchUrl = [Uri]::UnescapeDataString($buildSearchUrlMethod.Invoke($null, @('wukong', 'en-kz')))
Assert-Contains $customSearchUrl '"countryCode":"KZ"' 'A valid custom Store locale did not use its country.'
Assert-Contains $customSearchUrl '"languageCode":"en"' 'A valid custom Store locale did not use its language.'

$searchError = Parse-SearchResponse (Read-Fixture 'search-response-error.json')
Assert-Equal 0 $searchError.Results.Count 'An API error response returned games.'
Assert-Equal 'PersistedQueryNotFound' $searchError.Error 'The Store API error was not exposed.'

$provider = [Activator]::CreateInstance($providerType, @($null, $null))
$match = $provider.GetMatchingGame('Black Myth: Wukong', $search.Results)
Assert-Equal 'Black Myth: Wukong' $match.Name 'The base game did not outrank the deluxe edition.'

$remake = New-StoreResult 'Final Fantasy VII Remake' 'FULL_GAME'
$original = New-StoreResult 'Final Fantasy VII' 'FULL_GAME'
$matches = New-StoreResultList @($original, $remake)
$match = $provider.GetMatchingGame('Final Fantasy 7 Remake', $matches)
Assert-Equal 'Final Fantasy VII Remake' $match.Name 'The remake did not outrank the original game.'

$baseGame = New-StoreResult 'Like a Dragon: Pirate Yakuza in Hawaii PS4 & PS5' 'FULL_GAME'
$demo = New-StoreResult 'Like a Dragon: Pirate Yakuza in Hawaii Demo PS5' 'DEMO'
$matches = New-StoreResultList @($demo, $baseGame)
$match = $provider.GetMatchingGame('Like a Dragon: Pirate Yakuza in Hawaii', $matches)
Assert-Equal $baseGame.Name $match.Name 'Trailing PS4/PS5 labels prevented the full game from matching.'

$tombRaider = New-StoreResult 'Tomb Raider IV-VI Remastered PS4 & PS5' 'FULL_GAME'
$matches = New-StoreResultList @($tombRaider)
$match = $provider.GetMatchingGame('Tomb Raider IV-VI Remastered', $matches)
Assert-Equal $tombRaider.Name $match.Name 'Trailing platform labels prevented a Roman-number title from matching.'

$fullQuidditch = New-StoreResult 'Harry Potter: Quidditch Champions PS4 & PS5' 'FULL_GAME'
$bundleQuidditch = New-StoreResult 'Harry Potter: Quidditch Champions Deluxe Edition PS4 & PS5' 'GAME_BUNDLE'
$matches = New-StoreResultList @($bundleQuidditch, $fullQuidditch)
$match = $provider.GetMatchingGame('Harry Potter: Quidditch Champions', $matches)
Assert-Equal $fullQuidditch.Name $match.Name 'A game bundle outranked the matching full game.'

$gta = New-StoreResult 'Grand Theft Auto V (PS4™ & PS5™)' 'GAME_BUNDLE'
$matches = New-StoreResultList @($gta)
$match = $provider.GetMatchingGame('Grand Theft Auto V Enhanced', $matches)
Assert-Equal $gta.Name $match.Name 'A missing Store edition suffix discarded an otherwise strong game match.'

$massEffect = New-StoreResult 'Mass Effect™ Legendary Edition' 'FULL_GAME'
$andromeda = New-StoreResult 'Mass Effect™: Andromeda' 'FULL_GAME'
$matches = New-StoreResultList @($massEffect, $andromeda)
$match = $provider.GetMatchingGame('Mass Effect 2 (2010) Edition', $matches)
Assert-Null $match 'A different Mass Effect title was selected as an automatic match.'

$expectYouToDie = New-StoreResult 'I Expect You To Die' 'FULL_GAME'
$expectYouToDieTwo = New-StoreResult 'I Expect You To Die 2' 'FULL_GAME'
$matches = New-StoreResultList @($expectYouToDie, $expectYouToDieTwo)
$match = $provider.GetMatchingGame('I Expect You To Die 3', $matches)
Assert-Null $match 'A different I Expect You To Die sequel was selected as an automatic match.'

$trainStation = New-StoreResult 'Train Station Project' 'FULL_GAME'
$gasStation = New-StoreResult 'Gas Station Simulator' 'FULL_GAME'
$matches = New-StoreResultList @($trainStation, $gasStation)
$match = $provider.GetMatchingGame('Station to Station', $matches)
Assert-Null $match 'An unrelated station game was selected as an automatic match.'

$pageMetadata = $parsePageMethod.Invoke($null, @((Read-Fixture 'product-page-wukong.html')))
Assert-Equal 'Game Science Interactive Technology Co., Ltd.' $pageMetadata.Publisher 'The product page publisher was not parsed.'
Assert-Equal 90 $pageMetadata.CommunityScore 'The Store star rating was not converted to a 0-100 community score.'
Assert-Equal 3 $pageMetadata.Genres.Count 'The product page genres were not parsed.'
Assert-Equal 'Role Playing Games' $pageMetadata.Genres[0] 'The first genre was not retained.'
Assert-Equal 2024 $pageMetadata.ReleaseDate.Year 'The ISO release date was not parsed.'
Assert-Equal 8 $pageMetadata.ReleaseDate.Month 'The ISO release month was not parsed.'
Assert-Equal 20 $pageMetadata.ReleaseDate.Day 'The ISO release day was not parsed.'
Assert-Equal 'https://example.test/wukong-hero.jpg' $pageMetadata.BackgroundImageUrl 'The hero background image was not selected.'

Write-Output 'PSN Store parser and matcher fixture checks passed.'
