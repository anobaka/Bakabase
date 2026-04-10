using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Handlers;

public class DownloadInfoHandler(
    ILlmService llmService,
    ILogger<DownloadInfoHandler> logger)
    : IPostParseTargetHandler
{
    public PostParseTarget Target => PostParseTarget.DownloadInfo;

    private const string SystemPrompt = """
                                        你是一个专业的 HTML 解析助手。请分析帖子中的主题和评论，并提取出：
                                        帖子标题(title)，你可以结合主题内容适当调整标题，并且可以移除一些无关的内容。
                                        每个资源文件(resource)的下载链接(resource.link)，常见的下载链接包含baidu, pikpak, gofile, mega, 115, magnet等，下载链接链接不能是空字符串，不能是图片地址，不能包含这些关键字：dlsite。
                                        每个资源文件(resource)对应的提取码/访问码(resource.code)，一般来说帖子作者都会提供云存储下载链接，通常需要访问码才能访问。
                                        每个资源文件(resource)对应的解压码/解压缩密码/解压密码(resource.password)，千万不要漏掉解压密码，沒有解压密码将导致资源无法被解压缩。
                                        这是一个包含资源文件的HTML内容，请不要关注和下载资源无关的内容，所有资源信息不一定有标准的分隔符，所以请按照语义来找到正确的信息，确保不会遗漏。
                                        评论内也有可能会有资源，所以也请检查评论内容。
                                        请把这些资源(resource)放在资源列表中(resources)。
                                        请严格按照JSON 结构返回，不要返回非法的JSON。
                                        如果某个字段不存在，请使用 null 或者 空数组 [] 表示，而不要自行补全或推测。
                                        """;

    private const string UserPromptTemplate = """
                                              帖子标题：{title}
                                              主题HTML:
                                              {html}
                                              评论HTML:
                                              {userCommentHtmlList}
                                              """;

    public async Task<PostParseHandlerResult> HandleAsync(PostContent content, CancellationToken ct)
    {
        var commentHtml = string.Join(Environment.NewLine, content.CommentHtmlList ?? []);
        var userPrompt = UserPromptTemplate
            .Replace("{html}", content.MainHtml)
            .Replace("{userCommentHtmlList}", string.IsNullOrEmpty(commentHtml) ? "无" : commentHtml)
            .Replace("{title}", content.Title);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, userPrompt)
        };

        var response = await llmService.CompleteForFeatureAsync(AiFeature.PostParser, messages, ct: ct);
        var rawText = response.Text?.Trim() ?? "";
        var json = ExtractJson(rawText);

        DownloadInfoLlmResponse llmResult;
        try
        {
            llmResult = JsonConvert.DeserializeObject<DownloadInfoLlmResponse>(json)
                        ?? new DownloadInfoLlmResponse();
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                "Failed to parse JSON from LLM response: {Response}. Error: {Error}", rawText, ex.Message);
            throw new Exception($"Failed to parse download info from AI response: {ex.Message}");
        }

        Optimize(llmResult);

        // Title belongs at the task level, only resources go into result data
        var optimizedTitle = string.IsNullOrWhiteSpace(llmResult.Title) ? null : llmResult.Title;
        return new PostParseHandlerResult(new { resources = llmResult.Resources }, optimizedTitle);
    }

    private static string ExtractJson(string text)
    {
        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline >= 0)
                text = text[(firstNewline + 1)..];
            if (text.EndsWith("```"))
                text = text[..^3];
            text = text.Trim();
        }

        return text;
    }

    private static void Optimize(DownloadInfoLlmResponse result)
    {
        if (string.IsNullOrWhiteSpace(result.Title))
            result.Title = null;

        if (result.Resources != null)
        {
            result.Resources = result.Resources
                .Where(r => !string.IsNullOrEmpty(r.Link))
                .Select(r =>
                {
                    if (string.IsNullOrEmpty(r.Code)) r.Code = null;
                    if (string.IsNullOrEmpty(r.Password)) r.Password = null;
                    return r;
                })
                .ToList();

            if (result.Resources.Count == 0)
                result.Resources = null;
        }
    }
}
