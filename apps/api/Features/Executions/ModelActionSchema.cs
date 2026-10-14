namespace TeamFlow.Api.Features.Executions;

public static class ModelActionSchema
{
    public const string Json = """
        {"type":"object","properties":{"tool":{"type":"string","enum":["read","write","test","search","finish"]},"path":{"type":["string","null"]},"content":{"type":["string","null"]},"expectedHash":{"type":["string","null"]}},"required":["tool","path","content","expectedHash"],"additionalProperties":false}
        """;
}
