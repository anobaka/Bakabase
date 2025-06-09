using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Ai;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Ai;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus.Models;
using Bootstrap.Components.Configuration.Abstractions;
using CsQuery;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;
using Post = Bakabase.InsideWorld.Business.Components.PostParser.Models.Ai.Post;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Parsers;

public class SoulPlusPostParser(
    IBOptions<SoulPlusOptions> options,
    SoulPlusClient spClient,
    OllamaApiClientAccessor oaca,
    ILoggerFactory loggerFactory)
    : AbstractPostParser(loggerFactory)
{
    public override PostParserSource Source => PostParserSource.SoulPlus;

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

    private const string UserPrompt = """
                                      帖子标题：{title}
                                      主题HTML:
                                      {html}
                                      评论HTML:
                                      {userCommentHtmlList}
                                      """;

    public override async Task<PostParserTask> Parse(string link, CancellationToken ct)
    {
        var post = await spClient.GetPostAsync(link, ct);
        if (post.LockedContents != null)
        {
            var boughtSomething = false;
            foreach (var lc in post.LockedContents)
            {
                if (lc.IsBought)
                {
                    continue;
                }

                if (lc.Price > options.Value.AutoBuyThreshold)
                {
                    throw new Exception(
                        $"Failed due to price {lc.Price} is larger than auto buy threshold {options.Value.AutoBuyThreshold}");
                }

                // ignore fails on buying contents
                await spClient.BuyLockedContent(lc.Url!, ct);
                boughtSomething = true;
            }

            if (boughtSomething)
            {
                post = await spClient.GetPostAsync(link, ct);
            }
        }

        var html = post.Html;
        var cq = new CQ(html);
        var mainContent = WebUtility.HtmlDecode(cq["#read_tpc"].Html());
        var commentContents = cq[".tpc_content>.f14:not(#read_tpc)"].Select(c => WebUtility.HtmlDecode(c.Cq().Html()))
            .ToList();

        var analyzedPost = await Analyze(post.Title, mainContent, commentContents,ct);
        var task = new PostParserTask
        {
            Title = post.Title,
            Link = link,
            ParsedAt = DateTime.Now,
            Source = PostParserSource.SoulPlus,
            Items = analyzedPost.Resources?.Select(r => new PostParserTask.Item
                {AccessCode = r.Code, DecompressionPassword = r.Password, Link = r.Link}).ToList(),
        };

        return task;
    }

    protected async Task<Post> Analyze(string title, string mainHtml, List<string> commentHtmlList, CancellationToken ct)
    {
        var aiClient = oaca.Client;

        var schema = new
        {
            type = "object",
            properties = new
            {
                title = new {type = "string", description = "帖子的资源标题"},
                resources = new
                {
                    type = "array",
                    description = "资源列表",
                    items = new
                    {
                        type = "object",
                        description = "单个资源的下载信息",
                        properties = new
                        {
                            link = new {type = "string", description = "资源下载链接"},
                            code = new {type = new[] {"string", "null"}, description = "资源的提取码"},
                            password = new {type = new[] {"string", "null"}, description = "解压密码/解压缩密码/解压码"}
                        },
                        required = new[] {"link"}
                    }
                }
            },
            required = new[]
            {
                "title",
                "resources"
            }
        };

        var commentHtml = string.Join(Environment.NewLine, commentHtmlList ?? []);
        var userPrompt = UserPrompt
            .Replace("{html}", mainHtml)
            .Replace("{userCommentHtmlList}", string.IsNullOrEmpty(commentHtml) ? "无" : commentHtml)
            .Replace("{title}", title);
        // 2. 构造请求
        var request = new ChatRequest
        {
            Model = "deepseek-r1:32b",
            Messages = new List<Message>
            {
                new Message(ChatRole.System, SystemPrompt),
                new Message(ChatRole.User, userPrompt)
                // new Message(ChatRole.System, SystemPromptEn),
                // new Message(ChatRole.User, userPromptEn)
                // new Message(ChatRole.User, "hello")
            },
            Options = new RequestOptions()
            {
                Temperature = 0.0f
            },
            KeepAlive = "0",
            Format = schema, // 关键：结构化输出
            Think = false, // 关闭 <think> 推理段
            Stream = false // 只要完整一次输出
        };

        // 3. 调用并打印
        var response1 = aiClient.ChatAsync(request, ct);
        string response1Json = null!;
        await foreach (var r in response1!)
        {
            // Console.WriteLine(r.Message.Content);
            response1Json = r.Message.Content!;
        }

        try
        {
            return JsonConvert.DeserializeObject<Post>(response1Json)!.Optimize();
        }
        catch
        {
            Logger.LogWarning(
                $"Failed to deserialize json from response1, actual returned response is:{Environment.NewLine}{response1Json}{Environment.NewLine}Trying to correct json by second invoking...");
        }

        var request2 = new ChatRequest
        {
            Model = "deepseek-r1:8b",
            Messages = new List<Message>
            {
                new Message(ChatRole.System, "Text following may be an invalid json, fix it."),
                new Message(ChatRole.User, response1Json)
            },
            Options = new RequestOptions()
            {
                Temperature = 0.0f
            },
            KeepAlive = "0",
            Format = schema,
            Think = false, // 关闭 <think> 推理段
            Stream = false // 只要完整一次输出
        };

        // 3. 调用并打印
        var response2 = aiClient.ChatAsync(request2);
        var finalJson = string.Empty;
        await foreach (var r in response2!)
        {
            finalJson = r.Message.Content!;
        }

        return JsonConvert.DeserializeObject<Post>(finalJson)!.Optimize();
    }
}