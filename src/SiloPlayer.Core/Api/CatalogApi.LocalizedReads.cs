namespace SiloPlayer.Core.Api;

public partial class CatalogApi
{
    public long LocalizedReadGeneration => client.LocalizedReads.Generation;
}
