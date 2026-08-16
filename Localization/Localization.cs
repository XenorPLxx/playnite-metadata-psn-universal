using Playnite;

namespace Playnite;

public class LocalizedString : Markup.LocStringMarkup
{
    public LocalizedString() : base(UniversalPSNMetadata.UniversalPSNMetadataPlugin.Id)
    {
    }

    public LocalizedString(string stringId) : base(UniversalPSNMetadata.UniversalPSNMetadataPlugin.Id, stringId)
    {
    }
}

public static partial class Loc
{
    public static IPlayniteApi Api = null!;

    public static string GetString(string stringId) => Api.GetLocalizedString(stringId);

    public static string GetString(string stringId, params (string name, object value)[] args) => Api.GetLocalizedString(stringId, args);

    public static bool IsStringId(string id) => Api.IsLocalizedStringId(id);
}
