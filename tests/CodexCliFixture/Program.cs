if (args.SequenceEqual(new[] { "login", "status" })) { if (Environment.GetEnvironmentVariable("ConnectionStrings__Database") is not null) return 9; Console.WriteLine("Fixture login status only"); return 0; }
var output = Array.IndexOf(args, "--output-last-message");
if (output < 0 || args[^1] != "-" || !args.Contains("read-only") || Environment.GetEnvironmentVariable("ConnectionStrings__Database") is not null) return 9;
var prompt = await Console.In.ReadToEndAsync();
if (prompt == "timeout") await Task.Delay(TimeSpan.FromMinutes(1));
if (prompt == "fail") { Console.Error.WriteLine("sensitive fixture stderr"); return 7; }
await File.WriteAllTextAsync(args[output + 1], prompt == "empty" ? "" : "{\"tool\":\"finish\"}");
return 0;
