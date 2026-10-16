using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.Core.Services;

public sealed record ProfileAvatarUpload(string FileName, byte[] Bytes, string ContentType);

/// <summary>A single picker editor lifetime, bound to the verified primary and original browsing owner.</summary>
public sealed class ProfileAddSession(AuthApi api, CapturedProfileAuthority authority, IReadOnlyList<Library> libraries,
    ProfilesResponse capabilities, CancellationToken cancellationToken)
{
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    public IReadOnlyList<Library> Libraries { get; } = libraries;
    public ProfilesResponse Capabilities { get; } = capabilities;
    public CancellationToken CancellationToken => _lifetime.Token;
    public void Cancel() => _lifetime.Cancel();
    public bool IsCurrent => !CancellationToken.IsCancellationRequested && authority.IsCurrent;
    public bool IsOwnerCurrent => authority.IsCurrent;
    public Profile? CreatedProfile { get; private set; }

    public async Task<Profile> SaveAsync(CreateProfileRequest request, ProfileAvatarUpload? avatar)
    {
        if (!await _saveGate.WaitAsync(0, CancellationToken)) throw new InvalidOperationException("A profile save is already pending.");
        try
        {
            EnsureCurrent();
            // Keep the accepted identity if the subsequent upload fails. An explicit
            // retry must never create a second household profile.
            if (CreatedProfile == null)
                CreatedProfile = await api.CreateProfileAsync(authority, request, CancellationToken);
            EnsureCurrent();
            if (avatar != null)
            {
                var saved = await api.UploadProfileAvatarAsync(authority, CreatedProfile.Id,
                    avatar.FileName, avatar.Bytes, avatar.ContentType, CancellationToken);
                EnsureCurrent(); CreatedProfile = saved;
            }
            EnsureCurrent(); return CreatedProfile;
        }
        finally { _saveGate.Release(); }
    }

    public void EnsureCurrent()
    {
        CancellationToken.ThrowIfCancellationRequested();
        if (!authority.IsCurrent) throw new OperationCanceledException("The profile session changed.", CancellationToken);
    }
}
