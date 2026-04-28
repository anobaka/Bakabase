using System.Runtime.CompilerServices;
using System.Text.Json;
using Bakabase.Modules.AI.Components.Tools;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Bootstrap.Components.Orm.Infrastructures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.AI.Services;

public class ChatService<TDbContext>(
    ResourceService<TDbContext, ChatConversationDbModel, int> conversationOrm,
    ResourceService<TDbContext, ChatMessageDbModel, long> messageOrm,
    ILlmService llmService,
    LlmToolRegistry toolRegistry,
    ILogger<ChatService<TDbContext>> logger
) : IChatService where TDbContext : DbContext
{
    private const string SystemPrompt =
        """
        You are 小baka (Lil' Baka), a friendly and helpful assistant built into the Bakabase media manager app.
        You help users manage their media resources — searching, browsing, playing, organizing, and modifying properties.
        You have access to tools that let you interact with the user's resource library.
        Be concise and helpful. When showing resource results, include relevant details.
        When you use tools, explain what you're doing briefly.
        Respond in the same language the user uses.
        """;

    public async Task<ChatConversationDbModel> CreateConversationAsync(CancellationToken ct = default)
    {
        var conversation = new ChatConversationDbModel
        {
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var rsp = await conversationOrm.Add(conversation);
        return rsp.Data!;
    }

    public async Task<List<ChatConversationDbModel>> GetConversationsAsync(CancellationToken ct = default)
    {
        var all = await conversationOrm.GetAll(x => !x.IsArchived);
        return all.OrderByDescending(c => c.UpdatedAt).ToList();
    }

    public async Task<ChatConversationDbModel?> GetConversationAsync(int id, CancellationToken ct = default)
    {
        return await conversationOrm.GetByKey(id);
    }

    public async Task DeleteConversationAsync(int id, CancellationToken ct = default)
    {
        // Delete messages first
        var messages = await messageOrm.GetAll(x => x.ConversationId == id);
        foreach (var msg in messages)
        {
            await messageOrm.RemoveByKey(msg.Id);
        }
        await conversationOrm.RemoveByKey(id);
    }

    public async Task<ChatConversationDbModel> UpdateConversationTitleAsync(int id, string title,
        CancellationToken ct = default)
    {
        var conversation = await conversationOrm.GetByKey(id);
        if (conversation == null)
            throw new InvalidOperationException($"Conversation {id} not found");
        conversation.Title = title;
        conversation.UpdatedAt = DateTime.UtcNow;
        await conversationOrm.Update(conversation);
        return conversation;
    }

    public async Task<List<ChatMessageDbModel>> GetMessagesAsync(int conversationId, CancellationToken ct = default)
    {
        var all = await messageOrm.GetAll(x => x.ConversationId == conversationId);
        return all.OrderBy(m => m.CreatedAt).ToList();
    }

    public async IAsyncEnumerable<ChatStreamEvent> ChatAsync(
        int conversationId,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Save user message
        await messageOrm.Add(new ChatMessageDbModel
        {
            ConversationId = conversationId,
            Role = "user",
            Content = userMessage,
            CreatedAt = DateTime.UtcNow,
        });

        // Build message history
        var dbMessages = await GetMessagesAsync(conversationId, ct);
        var chatMessages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt)
        };

        foreach (var msg in dbMessages)
        {
            var role = msg.Role switch
            {
                "user" => ChatRole.User,
                "assistant" => ChatRole.Assistant,
                "system" => ChatRole.System,
                _ => ChatRole.User,
            };
            if (!string.IsNullOrEmpty(msg.Content))
            {
                chatMessages.Add(new ChatMessage(role, msg.Content));
            }
        }

        // Set up tool calling
        var options = await toolRegistry.WithEnabledToolsAsync(ct: ct);

        // Stream the response with tool call loop
        var fullResponse = new System.Text.StringBuilder();
        var allToolCalls = new List<object>();
        var allToolResults = new List<object>();
        var maxToolRounds = 10;

        for (var round = 0; round < maxToolRounds; round++)
        {
            var hasToolCalls = false;
            var pendingToolCalls = new List<FunctionCallContent>();

            await foreach (var update in llmService.CompleteStreamingForFeatureAsync(
                               AiFeature.Chat, chatMessages, options, ct))
            {
                // Handle text content
                if (update.Text is { Length: > 0 })
                {
                    fullResponse.Append(update.Text);
                    yield return new ChatStreamEvent
                    {
                        Type = ChatStreamEventType.TextDelta,
                        Text = update.Text,
                    };
                }

                // Collect tool calls
                if (update.Contents != null)
                {
                    foreach (var content in update.Contents)
                    {
                        if (content is FunctionCallContent functionCall)
                        {
                            hasToolCalls = true;
                            pendingToolCalls.Add(functionCall);
                        }
                    }
                }
            }

            if (!hasToolCalls) break;

            // Process collected tool calls outside the streaming loop
            foreach (var functionCall in pendingToolCalls)
            {
                var argsJson = functionCall.Arguments != null
                    ? JsonSerializer.Serialize(functionCall.Arguments)
                    : null;

                yield return new ChatStreamEvent
                {
                    Type = ChatStreamEventType.ToolCallStart,
                    ToolName = functionCall.Name,
                    ToolArgs = argsJson,
                };

                string resultStr;
                try
                {
                    // Find the matching tool function and invoke it
                    var tool = toolRegistry.GetAllTools()
                        .OfType<AIFunction>()
                        .FirstOrDefault(t => t.Name == functionCall.Name);

                    if (tool != null)
                    {
                        var args = functionCall.Arguments != null
                            ? new AIFunctionArguments(functionCall.Arguments)
                            : null;
                        var result = await tool.InvokeAsync(args, ct);
                        resultStr = result?.ToString() ?? "null";
                    }
                    else
                    {
                        resultStr = $"Error: Tool '{functionCall.Name}' not found";
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Tool call {ToolName} failed", functionCall.Name);
                    resultStr = $"Error: {ex.Message}";
                }

                // Collect for persistence
                allToolCalls.Add(new { name = functionCall.Name, args = argsJson });
                allToolResults.Add(new { name = functionCall.Name, result = resultStr });

                yield return new ChatStreamEvent
                {
                    Type = ChatStreamEventType.ToolCallResult,
                    ToolName = functionCall.Name,
                    ToolResult = resultStr,
                };

                // Add to history for next round
                chatMessages.Add(new ChatMessage(ChatRole.Assistant,
                    [new FunctionCallContent(functionCall.CallId, functionCall.Name, functionCall.Arguments)]));
                chatMessages.Add(new ChatMessage(ChatRole.Tool,
                    [new FunctionResultContent(functionCall.CallId, resultStr)]));
            }
        }

        // Save assistant message (always save if there's text or tool calls)
        var responseText = fullResponse.ToString();
        if (!string.IsNullOrWhiteSpace(responseText) || allToolCalls.Count > 0)
        {
            await messageOrm.Add(new ChatMessageDbModel
            {
                ConversationId = conversationId,
                Role = "assistant",
                Content = responseText,
                ToolCalls = allToolCalls.Count > 0 ? JsonSerializer.Serialize(allToolCalls) : null,
                ToolResults = allToolResults.Count > 0 ? JsonSerializer.Serialize(allToolResults) : null,
                CreatedAt = DateTime.UtcNow,
            });
        }

        // Update conversation title from first message if needed
        var conversation = await conversationOrm.GetByKey(conversationId);
        if (conversation != null)
        {
            if (string.IsNullOrEmpty(conversation.Title))
            {
                conversation.Title = userMessage.Length > 50 ? userMessage[..50] + "..." : userMessage;
            }
            conversation.UpdatedAt = DateTime.UtcNow;
            await conversationOrm.Update(conversation);
        }

        yield return new ChatStreamEvent { Type = ChatStreamEventType.Done };
    }
}
