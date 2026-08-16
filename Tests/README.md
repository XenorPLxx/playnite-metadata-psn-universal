# Fixture checks

`Fixtures/search-results-wukong.json` is a compact, anonymized PlayStation Store
GraphQL search response. It covers full-game versus deluxe-edition selection plus
cover/background media extraction.

`Fixtures/search-response-error.json` verifies the error shape returned if Sony
changes or retires the persisted search query.

Run the checks with:

```
dotnet run -c Release --project Tests/FixtureChecks
```

Add `-- --live` to additionally query the real PlayStation Store, which catches
changes to the persisted query, the request headers, and the response shape.

The fixture runner deliberately avoids adding a test framework dependency. The fixtures also avoid a full browser HAR,
which is too large and contains transient request metadata.
Run the P11 parser and matcher fixture checks with:

```powershell
dotnet run --project Tests/FixtureChecks/FixtureChecks.csproj --configuration Release
```
