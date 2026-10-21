namespace SiloPlayer.Core.Services;

public static class ExtraCardPresentation
{
    public static int ColumnCount(double viewportWidth) => viewportWidth >= 1280 ? 5
        : viewportWidth >= 1024 ? 4 : viewportWidth >= 640 ? 3 : 2;
}
