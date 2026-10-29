using System.Text.Json;

var output = Environment.GetEnvironmentVariable("SILO_LAUNCHER_FIXTURE_OUTPUT");
if (string.IsNullOrEmpty(output)) return 2;
var temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(args));
File.Move(temporary, output);
return 0;
