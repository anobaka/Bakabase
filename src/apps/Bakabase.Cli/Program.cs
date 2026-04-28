using Bakabase.Service.Components;
using Bootstrap.Components.Doc.Swagger;
using Microsoft.OpenApi.Writers;
using Newtonsoft.Json.Serialization;
using Swashbuckle.AspNetCore.Swagger;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: Bakabase.Cli <swagger|constants> <outPath>");
    return 1;
}

var command = args[0];
var outPath = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

switch (command)
{
    case "swagger":
        WriteSwagger(outPath);
        break;
    case "constants":
        WriteConstants(outPath);
        break;
    default:
        Console.Error.WriteLine($"Unknown command: {command}");
        return 1;
}

return 0;

static void WriteSwagger(string outPath)
{
    var builder = WebApplication.CreateBuilder();
    builder.Services
        .AddMvc()
        .AddApplicationPart(typeof(BakabaseStartup).Assembly)
        .AddNewtonsoftJson(t =>
        {
            t.SerializerSettings.ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false }
            };
        });
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddBootstrapSwaggerGen<BakabaseSwaggerCustomModelDocumentFilter>("v1", "API");

    var app = builder.Build();
    var provider = app.Services.GetRequiredService<ISwaggerProvider>();
    var doc = provider.GetSwagger("v1");

    using var sw = new StringWriter();
    doc.SerializeAsV3(new OpenApiJsonWriter(sw));
    File.WriteAllText(outPath, sw.ToString());
    Console.WriteLine($"swagger -> {outPath}");
}

static void WriteConstants(string outPath)
{
    File.WriteAllText(outPath, BakabaseConstantsGenerator.Generate());
    Console.WriteLine($"constants -> {outPath}");
}
