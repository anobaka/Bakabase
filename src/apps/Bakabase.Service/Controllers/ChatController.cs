using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Bakabase.Modules.AI.Components.Tools;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[Route("~/chat")]
public class ChatController(IChatService chatService, LlmToolRegistry toolRegistry) : Controller
{
    [HttpPost("conversations")]
    [SwaggerOperation(OperationId = "CreateChatConversation")]
    public async Task<SingletonResponse<ChatConversationDbModel>> CreateConversation(CancellationToken ct)
    {
        return new(await chatService.CreateConversationAsync(ct));
    }

    [HttpGet("conversations")]
    [SwaggerOperation(OperationId = "GetChatConversations")]
    public async Task<ListResponse<ChatConversationDbModel>> GetConversations(CancellationToken ct)
    {
        return new(await chatService.GetConversationsAsync(ct));
    }

    [HttpGet("conversations/{id:int}")]
    [SwaggerOperation(OperationId = "GetChatConversation")]
    public async Task<SingletonResponse<ChatConversationDbModel?>> GetConversation(int id, CancellationToken ct)
    {
        return new(await chatService.GetConversationAsync(id, ct));
    }

    [HttpDelete("conversations/{id:int}")]
    [SwaggerOperation(OperationId = "DeleteChatConversation")]
    public async Task<BaseResponse> DeleteConversation(int id, CancellationToken ct)
    {
        await chatService.DeleteConversationAsync(id, ct);
        return BaseResponseBuilder.Ok;
    }

    [HttpPut("conversations/{id:int}/title")]
    [SwaggerOperation(OperationId = "UpdateChatConversationTitle")]
    public async Task<SingletonResponse<ChatConversationDbModel>> UpdateTitle(
        int id, [FromBody] UpdateTitleRequest request, CancellationToken ct)
    {
        return new(await chatService.UpdateConversationTitleAsync(id, request.Title, ct));
    }

    [HttpGet("conversations/{id:int}/messages")]
    [SwaggerOperation(OperationId = "GetChatMessages")]
    public async Task<ListResponse<ChatMessageDbModel>> GetMessages(int id, CancellationToken ct)
    {
        return new(await chatService.GetMessagesAsync(id, ct));
    }

    [HttpPost("conversations/{id:int}/messages")]
    [SwaggerOperation(OperationId = "SendChatMessage")]
    public async Task SendMessage(int id, [FromBody] SendMessageRequest request)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        await Response.Body.FlushAsync();

        var ct = HttpContext.RequestAborted;

        try
        {
            await foreach (var evt in chatService.ChatAsync(id, request.Message, ct))
            {
                var json = JsonSerializer.Serialize(evt, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                });
                await Response.WriteAsync($"data: {json}\n\n", cancellationToken: ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected
        }
        catch (Exception ex)
        {
            var errorEvt = new ChatStreamEvent
            {
                Type = ChatStreamEventType.Error,
                Error = ex.Message,
            };
            var json = JsonSerializer.Serialize(errorEvt, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
            try
            {
                await Response.WriteAsync($"data: {json}\n\n", cancellationToken: ct);
                await Response.Body.FlushAsync(ct);
            }
            catch
            {
                // Client already disconnected
            }
        }
    }

    public record UpdateTitleRequest
    {
        public string Title { get; init; } = string.Empty;
    }

    public record SendMessageRequest
    {
        public string Message { get; init; } = string.Empty;
    }

    // === Tool Configuration ===

    [HttpGet("tools")]
    [SwaggerOperation(OperationId = "GetChatTools")]
    public async Task<ListResponse<ChatToolViewModel>> GetTools(CancellationToken ct)
    {
        var metadata = toolRegistry.GetAllMetadata();
        var configs = await toolRegistry.GetToolConfigsAsync(ct);
        var configMap = configs.ToDictionary(c => c.ToolName, c => c.IsEnabled);

        var viewModels = metadata.Select(m =>
        {
            bool isEnabled;
            if (configMap.TryGetValue(m.Name, out var configured))
                isEnabled = configured;
            else
                isEnabled = m.IsReadOnly; // default: read-only enabled, write disabled

            return new ChatToolViewModel
            {
                Name = m.Name,
                Description = m.Description,
                IsReadOnly = m.IsReadOnly,
                IsEnabled = isEnabled,
            };
        }).ToList();

        return new ListResponse<ChatToolViewModel>(viewModels);
    }

    [HttpPut("tools/{toolName}/enabled")]
    [SwaggerOperation(OperationId = "SetChatToolEnabled")]
    public async Task<BaseResponse> SetToolEnabled(string toolName, [FromBody] SetToolEnabledRequest request,
        CancellationToken ct)
    {
        await toolRegistry.SaveToolConfigAsync(toolName, request.IsEnabled, ct);
        return BaseResponseBuilder.Ok;
    }

    public record SetToolEnabledRequest
    {
        public bool IsEnabled { get; init; }
    }

    public record ChatToolViewModel
    {
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public bool IsReadOnly { get; init; }
        public bool IsEnabled { get; init; }
    }
}
