# Fixture checks

`Fixtures/search-results-wukong.json` is a compact, anonymized PlayStation Store
GraphQL search response. It covers full-game versus deluxe-edition selection plus
cover/background media extraction.

`Fixtures/search-response-error.json` verifies the error shape returned if Sony
changes or retires the persisted search query.

After a Release build, run the checks from PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tests\Run-FixtureChecks.ps1
```

The fixture runner deliberately avoids adding a test framework dependency to
this legacy .NET Framework plugin. The fixtures also avoid a full browser HAR,
which is too large and contains transient request metadata.
