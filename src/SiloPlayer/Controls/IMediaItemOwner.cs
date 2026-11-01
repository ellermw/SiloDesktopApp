using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Controls;

/// <summary>Exposes a rebindable card's current item and synchronous identity changes.</summary>
public interface IMediaItemOwner
{
    MediaItem? MediaItem { get; }
    event Action? MediaItemChanged;
}
