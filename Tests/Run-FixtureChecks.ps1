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

$searchError = Parse-SearchResponse (Read-Fixture 'search-response-error.json')
Assert-Equal 0 $searchError.Results.Count 'An API error response returned games.'
Assert-Equal 'PersistedQueryNotFound' $searchError.Error 'The Store API error was not exposed.'

$provider = [Activator]::CreateInstance($providerType, @($null, $null))
$match = $provider.GetMatchingGame('Black Myth: Wukong', $search.Results)
Assert-Equal 'Black Myth: Wukong' $match.Name 'The base game did not outrank the deluxe edition.'

$remake = [Activator]::CreateInstance($resultType)
$remake.Name = 'Final Fantasy VII Remake'
$remake.StoreDisplayClassification = 'FULL_GAME'
$remake.Platforms = [System.Collections.Generic.List[string]]::new([string[]]@('PS5'))
$original = [Activator]::CreateInstance($resultType)
$original.Name = 'Final Fantasy VII'
$original.StoreDisplayClassification = 'FULL_GAME'
$original.Platforms = [System.Collections.Generic.List[string]]::new([string[]]@('PS5'))
$matches = [System.Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($resultType))
$matches.Add($original)
$matches.Add($remake)
$match = $provider.GetMatchingGame('Final Fantasy 7 Remake', $matches)
Assert-Equal 'Final Fantasy VII Remake' $match.Name 'The remake did not outrank the original game.'

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
