using System.Net;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;

namespace Bakabase.Modules.ThirdParty.Tests.Bilibili;

[TestClass]
public class BilibiliApiCodesTests
{
    [DataTestMethod]
    [DataRow(0, BilibiliApiCodeClass.Ok)]
    [DataRow(-352, BilibiliApiCodeClass.RiskControl)]
    [DataRow(-412, BilibiliApiCodeClass.RiskControl)]
    [DataRow(-509, BilibiliApiCodeClass.RiskControl)]
    [DataRow(-799, BilibiliApiCodeClass.RiskControl)]
    [DataRow(-401, BilibiliApiCodeClass.RiskControl)]
    [DataRow(-500, BilibiliApiCodeClass.ServiceBusy)]
    [DataRow(-503, BilibiliApiCodeClass.ServiceBusy)]
    [DataRow(-504, BilibiliApiCodeClass.ServiceBusy)]
    [DataRow(-8888, BilibiliApiCodeClass.ServiceBusy)]
    [DataRow(-112, BilibiliApiCodeClass.ServiceBusy)]
    [DataRow(-702, BilibiliApiCodeClass.ServiceBusy)]
    [DataRow(-101, BilibiliApiCodeClass.NotLoggedIn)]
    [DataRow(62002, BilibiliApiCodeClass.ContentState)]
    [DataRow(62004, BilibiliApiCodeClass.ContentState)]
    [DataRow(62012, BilibiliApiCodeClass.ContentState)]
    [DataRow(87008, BilibiliApiCodeClass.ContentState)]
    [DataRow(-404, BilibiliApiCodeClass.ContentState)]
    [DataRow(-10403, BilibiliApiCodeClass.ContentState)]
    [DataRow(-403, BilibiliApiCodeClass.ContentState)]
    [DataRow(-400, BilibiliApiCodeClass.ContentState)]
    [DataRow(-12345, BilibiliApiCodeClass.Unknown)]
    [DataRow(-1, BilibiliApiCodeClass.Unknown)]
    [DataRow(1, BilibiliApiCodeClass.Unknown)]
    [DataRow(11010, BilibiliApiCodeClass.Unknown)]
    public void EveryRowOfTheTable(int code, BilibiliApiCodeClass expected)
    {
        Assert.AreEqual(expected, BilibiliApiCodes.Classify(code, null));
        Assert.AreEqual(expected, BilibiliApiCodes.Classify(code, ""));
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(62002)]
    [DataRow(-12345)]
    public void AVoucherIsRiskControlWhateverTheCode(int code)
    {
        Assert.AreEqual(BilibiliApiCodeClass.RiskControl, BilibiliApiCodes.Classify(code, "voucher_x"));
    }

    [DataTestMethod]
    [DataRow(HttpStatusCode.OK, BilibiliApiCodeClass.Ok)]
    [DataRow(HttpStatusCode.PreconditionFailed, BilibiliApiCodeClass.RiskControl)]
    [DataRow(HttpStatusCode.Forbidden, BilibiliApiCodeClass.RiskControl)]
    [DataRow(HttpStatusCode.RequestTimeout, BilibiliApiCodeClass.ServiceBusy)]
    [DataRow(HttpStatusCode.TooManyRequests, BilibiliApiCodeClass.ServiceBusy)]
    [DataRow(HttpStatusCode.InternalServerError, BilibiliApiCodeClass.ServiceBusy)]
    [DataRow(HttpStatusCode.BadGateway, BilibiliApiCodeClass.ServiceBusy)]
    [DataRow(HttpStatusCode.NotFound, BilibiliApiCodeClass.Unknown)]
    [DataRow(HttpStatusCode.BadRequest, BilibiliApiCodeClass.Unknown)]
    public void ApiHttpStatuses(HttpStatusCode status, BilibiliApiCodeClass expected)
    {
        Assert.AreEqual(expected, BilibiliApiCodes.ClassifyApiHttpStatus(status));
    }

    [TestMethod]
    public void UnexpectedCodesThrowTransientOnlyForRiskAndBusy()
    {
        var risk = BilibiliApiCodes.UnexpectedCode("playurl", -352, "-352");
        Assert.IsInstanceOfType(risk, typeof(BilibiliTemporarilyUnavailableException));
        Assert.AreEqual(BilibiliTemporaryFailureKind.RiskControl, ((BilibiliTemporarilyUnavailableException) risk).Kind);
        Assert.IsTrue(TransientNetworkError.IsTransient(risk));

        var busy = BilibiliApiCodes.UnexpectedCode("playurl", -8888, null);
        Assert.AreEqual(BilibiliTemporaryFailureKind.ServiceBusy, ((BilibiliTemporarilyUnavailableException) busy).Kind);

        foreach (var code in new[] {-12345, -101, -403})
        {
            var fatal = BilibiliApiCodes.UnexpectedCode("playurl", code, "x");
            Assert.IsInstanceOfType(fatal, typeof(BilibiliApiException));
            Assert.IsFalse(TransientNetworkError.IsTransient(fatal));
            BilibiliSecrets.AssertClean(fatal);
        }
    }
}
