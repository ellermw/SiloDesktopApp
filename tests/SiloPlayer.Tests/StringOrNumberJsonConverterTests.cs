using System.Text.Json;
using SiloPlayer.Core.Json;

namespace SiloPlayer.Tests;

public sealed class StringOrNumberJsonConverterTests
{
    [Fact]
    public void Read_PreservesUnsignedValueBeyondInt64Exactly()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new StringOrNumberJsonConverter());

        var value = JsonSerializer.Deserialize<string>("18446744073709551615", options);

        Assert.Equal("18446744073709551615", value);
    }
}
