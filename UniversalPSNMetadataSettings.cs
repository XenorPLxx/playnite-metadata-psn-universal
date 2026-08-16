using CommunityToolkit.Mvvm.ComponentModel;
using Playnite;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Controls;

namespace UniversalPSNMetadata;

public partial class UniversalPSNMetadataSettings : ObservableObject
{
    /// <summary>Empty follows the Playnite UI language.</summary>
    [ObservableProperty] private string storeLocale = StoreLocaleOptions.Automatic;
    [ObservableProperty] private string storeLocaleOverride = string.Empty;
}

[INotifyPropertyChanged]
public partial class UniversalPSNMetadataSettingsHandler : PluginSettingsHandler
{
    private const string SettingsErrorNotification = "psnstore_settings_error";

    private static readonly ILogger logger = LogManager.GetLogger();
    private readonly UniversalPSNMetadataPlugin plugin;

    [ObservableProperty] private UniversalPSNMetadataSettings settings = new();
    public IReadOnlyList<StoreLocaleOption> StoreLocales { get; } = StoreLocaleOptions.All;

    public UniversalPSNMetadataSettingsHandler(UniversalPSNMetadataPlugin plugin)
    {
        this.plugin = plugin;
    }

    public override UserControl GetEditView(GetSettingsViewArgs args)
    {
        return new UniversalPSNMetadataSettingsView { DataContext = this };
    }

    public override Task BeginEditAsync(BeginEditArgs args)
    {
        Settings = Clone(plugin.Settings);
        return Task.CompletedTask;
    }

    public override Task CancelEditAsync(CancelEditArgs args)
    {
        Settings = Clone(plugin.Settings);
        return Task.CompletedTask;
    }

    public override Task EndEditAsync(EndEditArgs args)
    {
        if (plugin.SaveSettings(Clone(Settings)))
        {
            plugin.PlayniteApi.Notifications.Remove(SettingsErrorNotification);
        }
        else
        {
            // The dialog is already closing, so a notification outlives it where a dialog would not.
            plugin.PlayniteApi.Notifications.Add(new NotificationMessage(
                SettingsErrorNotification,
                Loc.psnstore_settings_save_failed(),
                NotificationSeverity.Error,
                async () => await plugin.PlayniteApi.MainView.OpenPluginSettingsAsync(UniversalPSNMetadataPlugin.Id)));
        }

        return Task.CompletedTask;
    }

    public override Task<ICollection<string>> VerifySettingsAsync(VerifySettingsArgs args)
    {
        var errors = new List<string>();
        if (!StoreLocaleOptions.IsSupportedOrAutomatic(Settings.StoreLocale))
        {
            errors.Add(Loc.psnstore_invalid_region());
        }

        if (!string.IsNullOrWhiteSpace(Settings.StoreLocaleOverride) &&
            !StoreLocaleOptions.IsValid(Settings.StoreLocaleOverride))
        {
            errors.Add(Loc.psnstore_invalid_override());
        }

        return Task.FromResult<ICollection<string>>(errors);
    }

    internal static UniversalPSNMetadataSettings LoadSettings(string userDataDir)
    {
        var path = Path.Combine(userDataDir, "settings.json");
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<UniversalPSNMetadataSettings>(File.ReadAllText(path)) ?? new UniversalPSNMetadataSettings()
                : new UniversalPSNMetadataSettings();
        }
        catch (Exception e)
        {
            logger.Error(e, "Failed to load Universal PSN Metadata settings.");
            return new UniversalPSNMetadataSettings();
        }
    }

    /// <summary>
    /// Writes the settings, reporting rather than throwing when the user data folder cannot be
    /// written. Throwing here would surface as the whole settings dialog failing to close.
    /// </summary>
    internal static bool SaveSettings(string userDataDir, UniversalPSNMetadataSettings settings)
    {
        try
        {
            Directory.CreateDirectory(userDataDir);
            File.WriteAllText(
                Path.Combine(userDataDir, "settings.json"),
                JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception e)
        {
            logger.Error(e, "Failed to save Universal PSN Metadata settings.");
            return false;
        }
    }

    private static UniversalPSNMetadataSettings Clone(UniversalPSNMetadataSettings source) => new()
    {
        StoreLocale = source.StoreLocale,
        StoreLocaleOverride = source.StoreLocaleOverride
    };
}

public sealed record StoreLocaleOption(string DisplayName, string Locale);

internal static class StoreLocaleOptions
{
    public const string Automatic = "";
    public const string DefaultLocale = "en-us";
    private static readonly Regex StoreLocalePattern = new(
        "^[a-z]{2,3}(?:-[a-z]{2,4})?-[a-z]{2}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<StoreLocaleOption> All { get; } =
    [
        new("Automatic (use Playnite language)", Automatic),
        new("Argentina — Español", "es-ar"), new("Australia — English", "en-au"), new("Austria — Deutsch", "de-at"),
        new("Bahrain — English", "en-bh"), new("Bahrain — العربية", "ar-bh"), new("Belgium — Français", "fr-be"), new("Belgium — Nederlands", "nl-be"),
        new("Bolivia — Español", "es-bo"), new("Brazil — Português", "pt-br"), new("Bulgaria — Български", "bg-bg"), new("Bulgaria — English", "en-bg"),
        new("Canada — English", "en-ca"), new("Canada — Français", "fr-ca"), new("Chile — Español", "es-cl"), new("China — 简体中文", "zh-hans-cn"),
        new("Colombia — Español", "es-co"), new("Costa Rica — Español", "es-cr"), new("Croatia — Hrvatski", "hr-hr"), new("Croatia — English", "en-hr"),
        new("Cyprus — English", "en-cy"), new("Czech Republic — Čeština", "cs-cz"), new("Czech Republic — English", "en-cz"),
        new("Denmark — Dansk", "da-dk"), new("Denmark — English", "en-dk"), new("Ecuador — Español", "es-ec"), new("El Salvador — Español", "es-sv"),
        new("Finland — Suomi", "fi-fi"), new("Finland — English", "en-fi"), new("France — Français", "fr-fr"), new("Germany — Deutsch", "de-de"),
        new("Greece — Ελληνικά", "el-gr"), new("Greece — English", "en-gr"), new("Guatemala — Español", "es-gt"), new("Honduras — Español", "es-hn"),
        new("Hong Kong — English", "en-hk"), new("Hong Kong — 简体中文", "zh-hans-hk"), new("Hong Kong — 繁體中文", "zh-hant-hk"),
        new("Hungary — Magyar", "hu-hu"), new("Hungary — English", "en-hu"), new("Iceland — English", "en-is"), new("India — English", "en-in"),
        new("Indonesia — English", "en-id"), new("Ireland — English", "en-ie"), new("Israel — עברית", "he-il"), new("Israel — English", "en-il"),
        new("Italy — Italiano", "it-it"), new("Japan — 日本語", "ja-jp"), new("Korea — 한국어", "ko-kr"), new("Kuwait — English", "en-kw"),
        new("Kuwait — العربية", "ar-kw"), new("Lebanon — English", "en-lb"), new("Lebanon — العربية", "ar-lb"),
        new("Luxembourg — Deutsch", "de-lu"), new("Luxembourg — Français", "fr-lu"), new("Malaysia — English", "en-my"), new("Malta — English", "en-mt"),
        new("Mexico — Español", "es-mx"), new("Netherlands — Nederlands", "nl-nl"), new("New Zealand — English", "en-nz"), new("Nicaragua — Español", "es-ni"),
        new("Norway — Norsk", "no-no"), new("Norway — English", "en-no"), new("Oman — English", "en-om"), new("Oman — العربية", "ar-om"),
        new("Panama — Español", "es-pa"), new("Paraguay — Español", "es-py"), new("Peru — Español", "es-pe"), new("Philippines — English", "en-ph"),
        new("Poland — Polski", "pl-pl"), new("Poland — English", "en-pl"), new("Portugal — Português", "pt-pt"),
        new("Qatar — English", "en-qa"), new("Qatar — العربية", "ar-qa"), new("Romania — Română", "ro-ro"), new("Romania — English", "en-ro"),
        new("Russia — Русский", "ru-ru"), new("Saudi Arabia — English", "en-sa"), new("Saudi Arabia — العربية", "ar-sa"),
        new("Serbia — Српски", "sr-rs"), new("Singapore — English", "en-sg"), new("Slovakia — Slovenčina", "sk-sk"), new("Slovakia — English", "en-sk"),
        new("Slovenia — Slovenščina", "sl-si"), new("Slovenia — English", "en-si"), new("South Africa — English", "en-za"),
        new("Spain — Español", "es-es"), new("Sweden — Svenska", "sv-se"), new("Sweden — English", "en-se"),
        new("Switzerland — Deutsch", "de-ch"), new("Switzerland — Français", "fr-ch"), new("Switzerland — Italiano", "it-ch"),
        new("Taiwan — English", "en-tw"), new("Taiwan — 繁體中文", "zh-hant-tw"), new("Thailand — ไทย", "th-th"), new("Thailand — English", "en-th"),
        new("Turkey — Türkçe", "tr-tr"), new("Turkey — English", "en-tr"), new("Ukraine — Українська", "uk-ua"), new("Ukraine — Русский", "ru-ua"),
        new("United Arab Emirates — English", "en-ae"), new("United Arab Emirates — العربية", "ar-ae"), new("United Kingdom — English", "en-gb"),
        new("United States — English", "en-us"), new("Uruguay — Español", "es-uy"), new("Vietnam — English", "en-vn")
    ];

    public static bool IsSupported(string? locale) => All.Any(option => string.Equals(option.Locale, locale, StringComparison.OrdinalIgnoreCase));
    public static bool IsSupportedOrAutomatic(string? locale) => string.IsNullOrWhiteSpace(locale) || IsSupported(locale);
    public static string GetOrDefault(string? locale) => All.FirstOrDefault(option => string.Equals(option.Locale, locale, StringComparison.OrdinalIgnoreCase))?.Locale ?? DefaultLocale;
    public static bool IsValid(string? locale) => !string.IsNullOrWhiteSpace(locale) && StoreLocalePattern.IsMatch(locale.Trim());
    public static string Normalize(string? locale) => string.IsNullOrWhiteSpace(locale) ? string.Empty : locale.Trim().Replace('_', '-').ToLowerInvariant();
    public static string GetValidOrDefault(string? locale) => IsValid(locale) ? Normalize(locale) : DefaultLocale;

    public static string Resolve(string? overrideLocale, string? selectedLocale, string? playniteLanguage)
    {
        if (IsValid(overrideLocale)) return Normalize(overrideLocale);
        if (!string.IsNullOrWhiteSpace(selectedLocale)) return GetOrDefault(selectedLocale);

        var normalizedLanguage = Normalize(playniteLanguage);
        var exactLocale = All.FirstOrDefault(option => !string.IsNullOrEmpty(option.Locale) && string.Equals(option.Locale, normalizedLanguage, StringComparison.OrdinalIgnoreCase));
        if (exactLocale is not null) return exactLocale.Locale;

        // Chinese needs the region, not just the language: zh_TW and zh_HK are Traditional and have
        // their own Stores, so collapsing them to the language alone would pick mainland China.
        var chineseLocale = normalizedLanguage switch
        {
            "zh-tw" or "zh-hant-tw" or "zh-hant" => "zh-hant-tw",
            "zh-hk" or "zh-hant-hk" => "zh-hant-hk",
            _ => null
        };
        if (chineseLocale is not null) return GetOrDefault(chineseLocale);

        var language = normalizedLanguage.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var preferredLocale = language switch
        {
            "ar" => "ar-sa", "bg" => "bg-bg", "cs" => "cs-cz", "da" => "da-dk", "de" => "de-de", "el" => "el-gr", "en" => "en-us", "es" => "es-es",
            "fi" => "fi-fi", "fr" => "fr-fr", "he" => "he-il", "hr" => "hr-hr", "hu" => "hu-hu", "it" => "it-it", "ja" => "ja-jp", "ko" => "ko-kr",
            "nl" => "nl-nl", "no" => "no-no", "pl" => "pl-pl", "pt" => "pt-pt", "ro" => "ro-ro", "ru" => "ru-ru", "sk" => "sk-sk", "sl" => "sl-si",
            "sr" => "sr-rs", "sv" => "sv-se", "th" => "th-th", "tr" => "tr-tr", "uk" => "uk-ua", "zh" => "zh-hans-cn", _ => DefaultLocale
        };
        return GetOrDefault(preferredLocale);
    }
}
