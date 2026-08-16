using Playnite;

namespace UniversalPSNMetadata;

public sealed class UniversalPSNMetadataPlugin : Plugin
{
    public const string Id = "Xenor.UniversalPSNMetadata";
    public const string ExternalIdType = "playstation_store";
    public const string ExternalIdName = "PlayStation Store";

    public IPlayniteApi PlayniteApi { get; private set; } = null!;
    public UniversalPSNMetadataSettings Settings { get; private set; } = new();

    public UniversalPSNMetadataPlugin()
    {
        MetadataSettings = new MetadataSupport
        {
            Name = "PlayStation Store",
            SupportedDataIds =
            [
                BuiltInGameDataId.Description,
                BuiltInGameDataId.DesktopBackground,
                BuiltInGameDataId.CommunityScore,
                BuiltInGameDataId.DesktopCover,
                BuiltInGameDataId.Genres,
                BuiltInGameDataId.DesktopIcon,
                BuiltInGameDataId.ExternalIds,
                BuiltInGameDataId.Links,
                BuiltInGameDataId.Publishers,
                BuiltInGameDataId.ReleaseDate
            ]
        };
    }

    public override Task InitializeAsync(InitializeArgs args)
    {
        Loc.Api = args.Api;
        PlayniteApi = args.Api;
        Settings = UniversalPSNMetadataSettingsHandler.LoadSettings(PlayniteApi.UserDataDir);
        return Task.CompletedTask;
    }

    public override Task<MetadataProvider?> GetMetadataProviderAsync(GetMetadataProviderArgs args)
    {
        var locale = StoreLocaleOptions.Resolve(
            Settings.StoreLocaleOverride,
            Settings.StoreLocale,
            PlayniteApi.Settings.Language);
        return Task.FromResult<MetadataProvider?>(new UniversalPSNMetadataProvider(PlayniteApi, args, locale));
    }

    public override Task<PluginSettingsHandler?> GetSettingsHandlerAsync(GetSettingsHandlerArgs args)
    {
        return Task.FromResult<PluginSettingsHandler?>(new UniversalPSNMetadataSettingsHandler(this));
    }

    internal bool SaveSettings(UniversalPSNMetadataSettings settings)
    {
        Settings = settings;
        return UniversalPSNMetadataSettingsHandler.SaveSettings(PlayniteApi.UserDataDir, settings);
    }
}
