using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services;

public enum DetailFailureKind { None, NotFound, AccessDenied, Transient }

public static class DetailFailurePolicy
{
    public static DetailFailureKind Classify(Exception error) => error is ApiException api ? api.StatusCode switch
    { 404 => DetailFailureKind.NotFound, 403 => DetailFailureKind.AccessDenied, _ => DetailFailureKind.Transient } : DetailFailureKind.Transient;

    public static string Message(DetailFailureKind failure) => failure switch
    {
        DetailFailureKind.NotFound => "Item not found. It may have been removed from the library.",
        DetailFailureKind.AccessDenied => "This item isn't available to this profile.",
        _ => "Silo couldn't load this item. Check the connection and try again."
    };
}
