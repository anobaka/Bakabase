using System.IO.Compression;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AdaptiveAssessmentInventoryTests
{
    [DataTestMethod]
    [DataRow("audio.png", "89504E470D0A1A0A")]
    [DataRow("audio.jpeg", "FFD8FFE00010")]
    public async Task Verified_image_packaging_is_bound_to_its_leaf_and_blocks_an_unopened_final_vote(
        string image, string magic)
    {
        using var fixture = new FormatEvidenceFixture();
        var prefix = Convert.FromHexString(magic);
        var bytes = ImageWithStoredZip(prefix);
        var input = fixture.Input((image, bytes, bytes.LongLength), ("captions/1.srt", [1, 2], 2L));
        var model = new EvidenceModel();
        var service = new AdaptiveFileProcessingAssessmentService(model, NullLogger<AdaptiveFileProcessingAssessmentService>.Instance);
        var plan = await service.AssessAsync(input, "Unpack audio", 0, inspectImagePackaging: true);
        Assert.IsTrue(plan.ShouldContinue, plan.ValidationError);
        Assert.AreEqual(image, plan.Operations.Single().Input);
        StringAssert.Contains(model.UserMessage, "imagePackagingEvidence:\n{\"inputId\":1,\"archiveFormat\":\".zip\",\"zipOffset\":" + prefix.Length + "}");
        var final = new AdaptiveFileProcessingAssessmentService(
            new PasswordModel("""{"status":"final","reason":"looks like a cover","operations":[]}"""),
            NullLogger<AdaptiveFileProcessingAssessmentService>.Instance);
        var review = await final.AssessAsync(input, null, 0, inspectImagePackaging: true);
        Assert.AreEqual("ARCHIVE_PACKAGING_REVIEW_REQUIRED", review.DiagnosticCode);
        Assert.IsTrue(review.NeedsHumanReview);
        Assert.AreEqual(0, review.DeleteAfterSuccess.Count);
        Assert.IsTrue((await final.AssessAsync(input, null, 0)).IsFinal);
    }

    [DataTestMethod]
    [DataRow("game.exe")]
    [DataRow("another.jpg")]
    public async Task Galleries_and_applications_are_excluded_even_when_one_image_has_a_verified_zip(string companion)
    {
        using var fixture = new FormatEvidenceFixture();
        var bytes = ImageWithStoredZip(Convert.FromHexString("FFD8FF"));
        var input = fixture.Input(("image.jpg", bytes, bytes.LongLength), (companion, [0], 1));
        Assert.IsNull(await AdaptiveFileProcessingAssessmentService.InspectImagePackagingAsync(input, ["image.jpg", companion], default));
    }

    [TestMethod]
    public async Task Media_magic_and_matching_local_and_central_directory_are_required()
    {
        using var fixture = new FormatEvidenceFixture();
        var bytes = ImageWithStoredZip("not an image"u8.ToArray());
        var input = fixture.Input("cover.png", bytes);
        Assert.IsNull(await AdaptiveFileProcessingAssessmentService.InspectImagePackagingAsync(input, ["cover.png"], default));
        Convert.FromHexString("89504E470D0A1A0A").CopyTo(bytes, 0);
        bytes[ZipStructureFixture.Find(bytes, 0x02014b50)] = 0;
        input = fixture.Input("cover.png", bytes);
        Assert.IsNull(await AdaptiveFileProcessingAssessmentService.InspectImagePackagingAsync(input, ["cover.png"], default));
    }

    private static byte[] ImageWithStoredZip(byte[] prefix)
    {
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        using (var file = archive.CreateEntry("audio.dat", CompressionLevel.NoCompression).Open())
        {
            var chunk = new byte[64 * 1024];
            for (var i = 0; i < 256; i++) file.Write(chunk);
        }
        return [..prefix, ..zip.ToArray()];
    }
}
