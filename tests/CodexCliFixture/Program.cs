if (args.SequenceEqual(new[] { "auth", "status" })) { if (Environment.GetEnvironmentVariable("ConnectionStrings__Database") is not null) return 9; Console.WriteLine("{\"loggedIn\":true}"); return 0; }
if (args.Contains("--print"))
{
    if (args.Contains("--bare") || !args.Contains("--restricted") || !args.Contains("--safe-mode") || !args.Contains("--strict-mcp-config") || !args.Contains("--no-session-persistence")
        || args[Array.IndexOf(args, "--tools") + 1] != "" || args[Array.IndexOf(args, "--disallowedTools") + 1] != "Bash,PowerShell,Read,Write,Edit,WebFetch,WebSearch,Agent,Task"
        || Environment.GetEnvironmentVariable("ConnectionStrings__Database") is not null) return 9;
    var input = await Console.In.ReadToEndAsync();
    if (input == "verify-claude-settings" && (args[Array.IndexOf(args, "--model") + 1] != "sonnet" || args[Array.IndexOf(args, "--effort") + 1] != "high")) return 9;
    if (input == "fail") { Console.Error.WriteLine("sensitive fixture output"); return 7; }
    if (args.Contains("--json-schema"))
    {
        if (args[Array.IndexOf(args, "--output-format") + 1] != "json") return 9;
        using var schema = System.Text.Json.JsonDocument.Parse(args[Array.IndexOf(args, "--json-schema") + 1]);
        if (schema.RootElement.GetProperty("properties").GetProperty("tool").GetProperty("enum").GetArrayLength() != 5) return 9;
        Console.WriteLine(input == "invalid-envelope" ? "{\"result\":\"not structured\"}" : "{\"is_error\":false,\"usage\":{\"input_tokens\":2,\"cache_creation_input_tokens\":100,\"cache_read_input_tokens\":500,\"output_tokens\":4},\"structured_output\":{\"tool\":\"finish\",\"path\":null,\"content\":null,\"expectedHash\":null}}");
    }
    else Console.WriteLine("{\"tool\":\"finish\"}");
    return 0;
}
if (args.SequenceEqual(new[] { "login", "status" })) { if (Environment.GetEnvironmentVariable("ConnectionStrings__Database") is not null) return 9; Console.WriteLine("Fixture login status only"); return 0; }
var output = Array.IndexOf(args, "--output-last-message");
if (output < 0 || args[^1] != "-" || !args.Contains("read-only") || Environment.GetEnvironmentVariable("ConnectionStrings__Database") is not null) return 9;
if (args.Contains("--output-schema"))
{
    var schemaPath = args[Array.IndexOf(args, "--output-schema") + 1];
    using var schema = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(schemaPath));
    if (schema.RootElement.GetProperty("required").GetArrayLength() != 4) return 9;
}
var prompt = await Console.In.ReadToEndAsync();
if (prompt == "verify-codex-settings" && (args[Array.IndexOf(args, "--model") + 1] != "fixture-model" || !args.Contains("model_reasoning_effort=\"medium\""))) return 9;
if (prompt == "timeout") await Task.Delay(TimeSpan.FromMinutes(1));
if (prompt == "fail") { Console.Error.WriteLine("sensitive fixture stderr"); return 7; }
if (args.Contains("--json")) Console.WriteLine("{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":1000,\"cached_input_tokens\":800,\"output_tokens\":20}}");
await File.WriteAllTextAsync(args[output + 1], prompt == "empty" ? "" : "{\"tool\":\"finish\"}");
return 0;
