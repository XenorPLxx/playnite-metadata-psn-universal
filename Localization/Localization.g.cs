namespace Playnite;

public static partial class Loc
{

    /// <summary>
    /// PlayStation Store region and language
    /// </summary>
    public static string psnstore_region_label()
    {
        return GetString("psnstore_region_label");
    }
    /// <summary>
    /// Automatic follows the Playnite UI language. The selected locale is used for Store searches, links, and localized metadata such as descriptions.
    /// </summary>
    public static string psnstore_region_description()
    {
        return GetString("psnstore_region_description");
    }
    /// <summary>
    /// Store locale override (optional)
    /// </summary>
    public static string psnstore_override_label()
    {
        return GetString("psnstore_override_label");
    }
    /// <summary>
    /// Use a Store URL locale such as en-gb or zh-hant-tw. When set, it overrides the selection above.
    /// </summary>
    public static string psnstore_override_description()
    {
        return GetString("psnstore_override_description");
    }
    /// <summary>
    /// PlayStation Store Search
    /// </summary>
    public static string psnstore_search_caption()
    {
        return GetString("psnstore_search_caption");
    }
    /// <summary>
    /// Select a supported PlayStation Store region and language.
    /// </summary>
    public static string psnstore_invalid_region()
    {
        return GetString("psnstore_invalid_region");
    }
    /// <summary>
    /// Enter a Store locale such as en-gb or zh-hant-tw, or leave the override empty.
    /// </summary>
    public static string psnstore_invalid_override()
    {
        return GetString("psnstore_invalid_override");
    }
    /// <summary>
    /// PlayStation Store metadata settings could not be saved. Check that the Playnite user data folder is writable.
    /// </summary>
    public static string psnstore_settings_save_failed()
    {
        return GetString("psnstore_settings_save_failed");
    }
}

public static partial class LocId
{

    /// <summary>
    /// PlayStation Store region and language
    /// </summary>
    public const string psnstore_region_label = "psnstore_region_label";
    /// <summary>
    /// Automatic follows the Playnite UI language. The selected locale is used for Store searches, links, and localized metadata such as descriptions.
    /// </summary>
    public const string psnstore_region_description = "psnstore_region_description";
    /// <summary>
    /// Store locale override (optional)
    /// </summary>
    public const string psnstore_override_label = "psnstore_override_label";
    /// <summary>
    /// Use a Store URL locale such as en-gb or zh-hant-tw. When set, it overrides the selection above.
    /// </summary>
    public const string psnstore_override_description = "psnstore_override_description";
    /// <summary>
    /// PlayStation Store Search
    /// </summary>
    public const string psnstore_search_caption = "psnstore_search_caption";
    /// <summary>
    /// Select a supported PlayStation Store region and language.
    /// </summary>
    public const string psnstore_invalid_region = "psnstore_invalid_region";
    /// <summary>
    /// Enter a Store locale such as en-gb or zh-hant-tw, or leave the override empty.
    /// </summary>
    public const string psnstore_invalid_override = "psnstore_invalid_override";
    /// <summary>
    /// PlayStation Store metadata settings could not be saved. Check that the Playnite user data folder is writable.
    /// </summary>
    public const string psnstore_settings_save_failed = "psnstore_settings_save_failed";
}
