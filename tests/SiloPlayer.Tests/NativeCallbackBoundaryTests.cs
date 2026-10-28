using System.Reflection;
using SiloPlayer.Player;

namespace SiloPlayer.Tests;

public sealed class NativeCallbackBoundaryTests
{
    [Theory]
    [InlineData("DirectReadCallback")]
    [InlineData("DirectSeekCallback")]
    [InlineData("DirectSizeCallback")]
    [InlineData("DirectCloseCallback")]
    [InlineData("DirectCancelCallback")]
    public void FailedManagedCookieLookupCannotEscapeTheCallback(string name)
    {
        var callback = (Delegate)typeof(MpvPlayer).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        object? result = null;
        var failure = Record.Exception(() => result = callback.DynamicInvoke(name switch
        {
            "DirectReadCallback" => new object[] { IntPtr.Zero, IntPtr.Zero, 1UL },
            "DirectSeekCallback" => new object[] { IntPtr.Zero, 0L },
            _ => new object[] { IntPtr.Zero }
        }));
        Assert.Null(failure);
        if (name is "DirectReadCallback" or "DirectSeekCallback" or "DirectSizeCallback")
            Assert.Equal(-1L, result);
    }
}
