using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace UniversalPSNMetadata
{
    public class UniversalPSNMetadataSettings : ObservableObject
    {
        private string storeLocale = StoreLocaleOptions.DefaultLocale;
        private string storeLocaleOverride = string.Empty;

        public string StoreLocale
        {
            get => storeLocale;
            set => SetValue(ref storeLocale, StoreLocaleOptions.GetOrDefault(value));
        }

        public string StoreLocaleOverride
        {
            get => storeLocaleOverride;
            set => SetValue(ref storeLocaleOverride, StoreLocaleOptions.Normalize(value));
        }
    }

    public class UniversalPSNMetadataSettingsViewModel : ObservableObject, ISettings
    {
        private readonly UniversalPSNMetadata plugin;
        private UniversalPSNMetadataSettings editingClone { get; set; }

        private UniversalPSNMetadataSettings settings;
        public IReadOnlyList<StoreLocaleOption> StoreLocales { get; } = StoreLocaleOptions.All;

        public UniversalPSNMetadataSettings Settings
        {
            get => settings;
            set
            {
                settings = value;
                OnPropertyChanged();
            }
        }

        public UniversalPSNMetadataSettingsViewModel(UniversalPSNMetadata plugin)
        {
            this.plugin = plugin;
            Settings = plugin.LoadPluginSettings<UniversalPSNMetadataSettings>()
                ?? new UniversalPSNMetadataSettings();
            Settings.StoreLocale = Settings.StoreLocale;
            Settings.StoreLocaleOverride = Settings.StoreLocaleOverride;
        }

        public void BeginEdit()
        {
            editingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit()
        {
            Settings = editingClone;
        }

        public void EndEdit()
        {
            plugin.SavePluginSettings(Settings);
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            if (!StoreLocaleOptions.IsSupported(Settings.StoreLocale))
            {
                errors.Add("Select a supported PlayStation Store region and language.");
            }

            if (!string.IsNullOrEmpty(Settings.StoreLocaleOverride) &&
                !StoreLocaleOptions.IsValid(Settings.StoreLocaleOverride))
            {
                errors.Add("Enter a Store locale such as en-gb or zh-hant-tw, or leave the override empty.");
            }

            return !errors.Any();
        }
    }

    public sealed class StoreLocaleOption
    {
        public string DisplayName { get; }
        public string Locale { get; }

        public StoreLocaleOption(string displayName, string locale)
        {
            DisplayName = displayName;
            Locale = locale;
        }
    }

    internal static class StoreLocaleOptions
    {
        public const string DefaultLocale = "en-us";
        private static readonly Regex StoreLocalePattern = new Regex(
            "^[a-z]{2,3}(?:-[a-z]{2,4})?-[a-z]{2}$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static IReadOnlyList<StoreLocaleOption> All { get; } = new List<StoreLocaleOption>
        {
            new StoreLocaleOption("Argentina — Español", "es-ar"),
            new StoreLocaleOption("Australia — English", "en-au"),
            new StoreLocaleOption("Austria — Deutsch", "de-at"),
            new StoreLocaleOption("Bahrain — English", "en-bh"),
            new StoreLocaleOption("Bahrain — العربية", "ar-bh"),
            new StoreLocaleOption("Belgium — Français", "fr-be"),
            new StoreLocaleOption("Belgium — Nederlands", "nl-be"),
            new StoreLocaleOption("Bolivia — Español", "es-bo"),
            new StoreLocaleOption("Brazil — Português", "pt-br"),
            new StoreLocaleOption("Bulgaria — Български", "bg-bg"),
            new StoreLocaleOption("Bulgaria — English", "en-bg"),
            new StoreLocaleOption("Canada — English", "en-ca"),
            new StoreLocaleOption("Canada — Français", "fr-ca"),
            new StoreLocaleOption("Chile — Español", "es-cl"),
            new StoreLocaleOption("China — 简体中文", "zh-hans-cn"),
            new StoreLocaleOption("Colombia — Español", "es-co"),
            new StoreLocaleOption("Costa Rica — Español", "es-cr"),
            new StoreLocaleOption("Croatia — Hrvatski", "hr-hr"),
            new StoreLocaleOption("Croatia — English", "en-hr"),
            new StoreLocaleOption("Cyprus — English", "en-cy"),
            new StoreLocaleOption("Czech Republic — Čeština", "cs-cz"),
            new StoreLocaleOption("Czech Republic — English", "en-cz"),
            new StoreLocaleOption("Denmark — Dansk", "da-dk"),
            new StoreLocaleOption("Denmark — English", "en-dk"),
            new StoreLocaleOption("Ecuador — Español", "es-ec"),
            new StoreLocaleOption("El Salvador — Español", "es-sv"),
            new StoreLocaleOption("Finland — Suomi", "fi-fi"),
            new StoreLocaleOption("Finland — English", "en-fi"),
            new StoreLocaleOption("France — Français", "fr-fr"),
            new StoreLocaleOption("Germany — Deutsch", "de-de"),
            new StoreLocaleOption("Greece — Ελληνικά", "el-gr"),
            new StoreLocaleOption("Greece — English", "en-gr"),
            new StoreLocaleOption("Guatemala — Español", "es-gt"),
            new StoreLocaleOption("Honduras — Español", "es-hn"),
            new StoreLocaleOption("Hong Kong — English", "en-hk"),
            new StoreLocaleOption("Hong Kong — 简体中文", "zh-hans-hk"),
            new StoreLocaleOption("Hong Kong — 繁體中文", "zh-hant-hk"),
            new StoreLocaleOption("Hungary — Magyar", "hu-hu"),
            new StoreLocaleOption("Hungary — English", "en-hu"),
            new StoreLocaleOption("Iceland — English", "en-is"),
            new StoreLocaleOption("India — English", "en-in"),
            new StoreLocaleOption("Indonesia — English", "en-id"),
            new StoreLocaleOption("Ireland — English", "en-ie"),
            new StoreLocaleOption("Israel — עברית", "he-il"),
            new StoreLocaleOption("Israel — English", "en-il"),
            new StoreLocaleOption("Italy — Italiano", "it-it"),
            new StoreLocaleOption("Japan — 日本語", "ja-jp"),
            new StoreLocaleOption("Korea — 한국어", "ko-kr"),
            new StoreLocaleOption("Kuwait — English", "en-kw"),
            new StoreLocaleOption("Kuwait — العربية", "ar-kw"),
            new StoreLocaleOption("Lebanon — English", "en-lb"),
            new StoreLocaleOption("Lebanon — العربية", "ar-lb"),
            new StoreLocaleOption("Luxembourg — Deutsch", "de-lu"),
            new StoreLocaleOption("Luxembourg — Français", "fr-lu"),
            new StoreLocaleOption("Malaysia — English", "en-my"),
            new StoreLocaleOption("Malta — English", "en-mt"),
            new StoreLocaleOption("Mexico — Español", "es-mx"),
            new StoreLocaleOption("Netherlands — Nederlands", "nl-nl"),
            new StoreLocaleOption("New Zealand — English", "en-nz"),
            new StoreLocaleOption("Nicaragua — Español", "es-ni"),
            new StoreLocaleOption("Norway — Norsk", "no-no"),
            new StoreLocaleOption("Norway — English", "en-no"),
            new StoreLocaleOption("Oman — English", "en-om"),
            new StoreLocaleOption("Oman — العربية", "ar-om"),
            new StoreLocaleOption("Panama — Español", "es-pa"),
            new StoreLocaleOption("Paraguay — Español", "es-py"),
            new StoreLocaleOption("Peru — Español", "es-pe"),
            new StoreLocaleOption("Philippines — English", "en-ph"),
            new StoreLocaleOption("Poland — Polski", "pl-pl"),
            new StoreLocaleOption("Poland — English", "en-pl"),
            new StoreLocaleOption("Portugal — Português", "pt-pt"),
            new StoreLocaleOption("Qatar — English", "en-qa"),
            new StoreLocaleOption("Qatar — العربية", "ar-qa"),
            new StoreLocaleOption("Romania — Română", "ro-ro"),
            new StoreLocaleOption("Romania — English", "en-ro"),
            new StoreLocaleOption("Russia — Русский", "ru-ru"),
            new StoreLocaleOption("Saudi Arabia — English", "en-sa"),
            new StoreLocaleOption("Saudi Arabia — العربية", "ar-sa"),
            new StoreLocaleOption("Serbia — Српски", "sr-rs"),
            new StoreLocaleOption("Singapore — English", "en-sg"),
            new StoreLocaleOption("Slovakia — Slovenčina", "sk-sk"),
            new StoreLocaleOption("Slovakia — English", "en-sk"),
            new StoreLocaleOption("Slovenia — Slovenščina", "sl-si"),
            new StoreLocaleOption("Slovenia — English", "en-si"),
            new StoreLocaleOption("South Africa — English", "en-za"),
            new StoreLocaleOption("Spain — Español", "es-es"),
            new StoreLocaleOption("Sweden — Svenska", "sv-se"),
            new StoreLocaleOption("Sweden — English", "en-se"),
            new StoreLocaleOption("Switzerland — Deutsch", "de-ch"),
            new StoreLocaleOption("Switzerland — Français", "fr-ch"),
            new StoreLocaleOption("Switzerland — Italiano", "it-ch"),
            new StoreLocaleOption("Taiwan — English", "en-tw"),
            new StoreLocaleOption("Taiwan — 繁體中文", "zh-hant-tw"),
            new StoreLocaleOption("Thailand — ไทย", "th-th"),
            new StoreLocaleOption("Thailand — English", "en-th"),
            new StoreLocaleOption("Turkey — Türkçe", "tr-tr"),
            new StoreLocaleOption("Turkey — English", "en-tr"),
            new StoreLocaleOption("Ukraine — Українська", "uk-ua"),
            new StoreLocaleOption("Ukraine — Русский", "ru-ua"),
            new StoreLocaleOption("United Arab Emirates — English", "en-ae"),
            new StoreLocaleOption("United Arab Emirates — العربية", "ar-ae"),
            new StoreLocaleOption("United Kingdom — English", "en-gb"),
            new StoreLocaleOption("United States — English", "en-us"),
            new StoreLocaleOption("Uruguay — Español", "es-uy"),
            new StoreLocaleOption("Vietnam — English", "en-vn")
        };

        public static bool IsSupported(string locale)
        {
            return All.Any(option => string.Equals(option.Locale, locale, StringComparison.OrdinalIgnoreCase));
        }

        public static string GetOrDefault(string locale)
        {
            var selected = All.FirstOrDefault(option => string.Equals(option.Locale, locale, StringComparison.OrdinalIgnoreCase));
            return selected?.Locale ?? DefaultLocale;
        }

        public static bool IsValid(string locale)
        {
            return !string.IsNullOrWhiteSpace(locale) && StoreLocalePattern.IsMatch(locale.Trim());
        }

        public static string Normalize(string locale)
        {
            return string.IsNullOrWhiteSpace(locale) ? string.Empty : locale.Trim().ToLowerInvariant();
        }

        public static string GetValidOrDefault(string locale)
        {
            return IsValid(locale) ? Normalize(locale) : DefaultLocale;
        }
    }
}
