using Bakabase.Modules.AI.Models.Db;

namespace Bakabase.Modules.AI.Services;

public interface IChatService
{
    Task<ChatConversationDbModel> CreateConversationAsync(CancellationToken ct = default);
    Task<List<ChatConversationDbModel>> GetConversationsAsync(CancellationToken ct = default);
    Task<ChatConversationDbModel?> GetConversationAsync(int id, CancellationToken ct = default);
    Task DeleteConversationAsync(int id, CancellationToken ct = default);
    Task<ChatConversationDbModel> UpdateConversationTitleAsync(int id, string title, CancellationToken ct = default);
    Task<List<ChatMessageDbModel>> GetMessagesAsync(int conversationId, CancellationToken ct = default);

    IAsyncEnumerable<ChatStreamEvent> ChatAsync(
        int conversationId,
        string userMessage,
        CancellationToken ct = default);
}

public record ChatStreamEvent
{
    public required ChatStreamEventType Type { get; init; }
    public string? Text { get; init; }
    public string? ToolName { get; init; }
    public string? ToolArgs { get; init; }
    public string? ToolResult { get; init; }
    /// <summary>
    /// JSON-serialized structured data for rich rendering (resource cards, etc.)
    /// </summary>
    public string? RichContent { get; init; }
    public string? Error { get; init; }
}

public enum ChatStreamEventType
{
    TextDelta = 1,
    ToolCallStart = 2,
    ToolCallResult = 3,
    Done = 4,
    Error = 5,
}
