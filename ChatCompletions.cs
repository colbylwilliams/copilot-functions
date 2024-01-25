namespace Copilot.Functions;

public class ChatCompletions
{
    public static ChatCompletions Final(string id) => new()
    {
        Id = id,
        Choices = [new() { Index = 0, Delta = new() { Content = string.Empty }, FinishReason = "stop" }]
    };

    public required string Id { get; set; }
    public string Object { get; set; } = "chat.completion.chunk";
    public long Created { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public string Model { get; set; } = "gpt-3.5-turbo-0613";
    public List<ChatChoice> Choices { get; set; } = [];
}

public class ChatChoice
{
    public required int Index { get; set; }
    public required ChatMessage Delta { get; set; }
    public string? FinishReason { get; set; } = null;
}

public class ChatMessage
{
    public required string Content { get; set; } = string.Empty;
}