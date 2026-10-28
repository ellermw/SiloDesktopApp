namespace SiloPlayer.Core.Models.Playback;

public class HdrCapabilityDetails
{
    public bool Hdr10 { get; set; }
    public bool Hdr10Plus { get; set; }
    public bool Hlg { get; set; }
    public List<int> DolbyVisionProfiles { get; set; } = [];
}

public class AudioPassthroughCapabilities
{
    public List<string> PassthroughCodecs { get; set; } = [];
    public bool SpatializerEnabled { get; set; }
    public int MaxChannels { get; set; }
}
