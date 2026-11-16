using System.Buffers.Binary;
using System.Text;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AdaptiveAssessmentInventoryTests
{
    [DataTestMethod]
    [DataRow("rename", "film.7z", true)]
    [DataRow("extract", null, true)]
    [DataRow("rename", "author-name.mp4", false)]
    public async Task Verified_movie_after_extraction_rejects_only_conflicting_model_operations(
        string operation, string? destination, bool reject)
    {
        using var fixture = new FormatEvidenceFixture();
        var input = fixture.Input("released.mp4", PackagedMovie());
        var response = System.Text.Json.JsonSerializer.Serialize(new {status = "continue",
            operations = new[] {new {type = operation, inputId = 1, destination}}});
        var model = new EvidenceModel(response);
        var decision = await new AdaptiveFileProcessingAssessmentService(model, NullLogger<AdaptiveFileProcessingAssessmentService>.Instance)
            .AssessAsync(input, null, 1, default, false, new(input.Id, input.Fingerprint,
                [new(1, "outer", "origin", input.Id, input.Fingerprint, [new("extractArchive", 1)])]));
        StringAssert.Contains(model.UserMessage, "recognizedMp4Container");
        Assert.AreEqual(reject, decision.NeedsHumanReview);
        if (reject) { Assert.AreEqual(0, decision.Operations.Count); StringAssert.Contains(decision.ValidationError!, "verified movie"); }
    }

    [DataTestMethod]
    [DataRow("no-extraction")] [DataRow("wrong-receipt")] [DataRow("raw-archive")] [DataRow("trailing-archive")]
    [DataRow("embedded-zip")] [DataRow("no-media-track")] [DataRow("opaque-container")]
    public async Task Unknown_inner_and_ambiguous_packages_remain_eligible_for_assessment(string scenario)
    {
        using var fixture = new FormatEvidenceFixture();
        var bytes = scenario switch
        {
            "raw-archive" => Convert.FromHexString("377ABCAF271C0004"),
            "trailing-archive" => PackagedMovie().Concat("PK\x03\x04"u8.ToArray()).ToArray(),
            "embedded-zip" => PackagedMovie("PK\x05\x06"u8.ToArray()),
            "no-media-track" => PackagedMovie(handler: "meta"),
            "opaque-container" => PackagedMovie().Concat(Mp4Box("uuid", new byte[64])).ToArray(),
            _ => PackagedMovie()
        };
        var input = fixture.Input("movie.mp4", bytes);
        var model = new EvidenceModel();
        var decision = await new AdaptiveFileProcessingAssessmentService(model, NullLogger<AdaptiveFileProcessingAssessmentService>.Instance)
            .AssessAsync(input, null, 1, default, false, new(input.Id, input.Fingerprint,
                [new(1, "outer", "origin", input.Id, scenario == "wrong-receipt" ? "stale" : input.Fingerprint,
                    [new(scenario == "no-extraction" ? "renameFile" : "extractArchive", 1)])]));
        Assert.IsFalse(model.UserMessage.Contains("recognizedMp4Container", StringComparison.Ordinal));
        Assert.IsTrue(decision.ShouldContinue);
    }

    private static byte[] PackagedMovie(byte[]? payload = null, string handler = "vide")
    {
        var type = Mp4Box("ftyp", "isom\0\0\0\0mp42"u8.ToArray());
        var track = Mp4Box("trak", [.. Mp4Box("tkhd", new byte[84]),
            .. Mp4Box("mdia", Mp4Box("hdlr", [.. new byte[8], .. Encoding.ASCII.GetBytes(handler), .. new byte[12]]))]);
        return [.. type, .. Mp4Box("moov", [.. Mp4Box("mvhd", new byte[100]), .. track]),
            .. Mp4Box("free", []), .. Mp4Box("mdat", payload ?? new byte[128])];
    }

    private static byte[] Mp4Box(string type, byte[] payload)
    {
        var bytes = new byte[payload.Length + 8];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)bytes.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(bytes, 4);
        payload.CopyTo(bytes, 8);
        return bytes;
    }
}
