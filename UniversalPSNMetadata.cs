using Playnite.SDK;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace UniversalPSNMetadata
{
  public class UniversalPSNMetadata : MetadataPlugin
  {
    private static readonly ILogger logger = LogManager.GetLogger();

    private UniversalPSNMetadataSettingsViewModel settings { get; set; }

    public override Guid Id { get; } = Guid.Parse("d3aab57b-3ece-4211-8dae-40e7470bdc4c");

    public override List<MetadataField> SupportedFields { get; } = new List<MetadataField>
        {
            MetadataField.Description,
            MetadataField.BackgroundImage,
            //MetadataField.CommunityScore,
            MetadataField.CoverImage,
            //MetadataField.CriticScore,
            //MetadataField.Developers,
            //MetadataField.Genres,
            //MetadataField.Icon,
            //MetadataField.Links,
            //MetadataField.Publishers,
            //MetadataField.ReleaseDate,
            //MetadataField.Features,
            //MetadataField.Name,
            //MetadataField.Platform,
            //MetadataField.Series
            // Include addition fields if supported by the metadata source
        };

    // Change to something more appropriate
    public override string Name => "PSN Store";

    public UniversalPSNMetadata(IPlayniteAPI api) : base(api)
    {
      settings = new UniversalPSNMetadataSettingsViewModel(this);
      Properties = new MetadataPluginProperties
      {
        HasSettings = true
      };
    }

    public override OnDemandMetadataProvider GetMetadataProvider(MetadataRequestOptions options)
    {
      return new UniversalPSNMetadataProvider(options, this);
    }

    public override ISettings GetSettings(bool firstRunSettings)
    {
      return settings;
    }

    public override UserControl GetSettingsView(bool firstRunSettings)
    {
      return new UniversalPSNMetadataSettingsView();
    }
  }
}