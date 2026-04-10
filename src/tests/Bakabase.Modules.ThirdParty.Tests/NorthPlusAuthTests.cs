using HcPresets = HttpCloak.Presets;
using HttpCloak;

namespace Bakabase.Modules.ThirdParty.Tests
{
    [TestClass]
    public class NorthPlusAuthTests
    {
        private const string TargetUrl = "https://www.north-plus.net/index.php";
        private const string LoggedInKeyword = "退出";

        // 🔥 在这里粘贴你从浏览器抓到的完整 Cookie 字符串
        private const string Cookie = "";

        private static Dictionary<string, string> BuildHeaders(string? cookie = null) => new()
        {
            {
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36 Edg/146.0.0.0"
            },
            { "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8" },
            { "Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8" },
            { "Cookie", cookie ?? Cookie }
        };

        #region Desktop 浏览器 TLS 指纹

        /// <summary>
        /// Chrome 各大版本 TLS 指纹
        /// </summary>
        [TestMethod]
        [DataRow(HcPresets.Chrome146, "Chrome146")]
        [DataRow(HcPresets.Chrome145, "Chrome145")]
        [DataRow(HcPresets.Chrome144, "Chrome144")]
        [DataRow(HcPresets.Chrome143, "Chrome143")]
        [DataRow(HcPresets.Chrome141, "Chrome141")]
        [DataRow(HcPresets.Chrome133, "Chrome133")]
        public void Auth_ChromeVersions(string preset, string label)
        {
            AssertLoggedIn(preset, label);
        }

        /// <summary>
        /// Chrome 146 平台变体 (Windows / Linux / macOS)
        /// </summary>
        [TestMethod]
        [DataRow(HcPresets.Chrome146Windows, "Chrome146-Windows")]
        [DataRow(HcPresets.Chrome146Linux, "Chrome146-Linux")]
        [DataRow(HcPresets.Chrome146MacOS, "Chrome146-macOS")]
        public void Auth_ChromePlatformVariants(string preset, string label)
        {
            AssertLoggedIn(preset, label);
        }

        /// <summary>
        /// Firefox / Safari 指纹
        /// </summary>
        [TestMethod]
        [DataRow(HcPresets.Firefox133, "Firefox133")]
        [DataRow(HcPresets.Safari18, "Safari18")]
        public void Auth_NonChromeBrowsers(string preset, string label)
        {
            AssertLoggedIn(preset, label);
        }

        #endregion

        #region 移动端 TLS 指纹

        [TestMethod]
        [DataRow(HcPresets.Chrome146Ios, "Chrome146-iOS")]
        [DataRow(HcPresets.Chrome146Android, "Chrome146-Android")]
        [DataRow(HcPresets.Safari18Ios, "Safari18-iOS")]
        [DataRow(HcPresets.Safari17Ios, "Safari17-iOS")]
        public void Auth_MobilePresets(string preset, string label)
        {
            AssertLoggedIn(preset, label);
        }

        #endregion

        #region HTTP 协议版本

        /// <summary>
        /// 对比不同 HTTP 协议版本 (h1 / h2 / h3) 的连通性
        /// </summary>
        [TestMethod]
        [DataRow("h1", "HTTP/1.1")]
        [DataRow("h2", "HTTP/2")]
        [DataRow("h3", "HTTP/3")]
        public void Auth_HttpVersions(string httpVersion, string label)
        {
            using var session = new Session(preset: HcPresets.Chrome146, httpVersion: httpVersion);
            var response = session.Get(TargetUrl, headers: BuildHeaders());

            Console.WriteLine($"[{label}] Status: {response.StatusCode}, Protocol: {response.Protocol}");
            Assert.AreEqual(200, response.StatusCode, $"[{label}] 请求失败");

            var isLoggedIn = response.Text.Contains(LoggedInKeyword);
            Console.WriteLine($"[{label}] 登录状态: {isLoggedIn}");
        }

        #endregion

        #region Session Cookie 持久化

        /// <summary>
        /// 验证 Session 的 Cookie 持久化：第一次带 Cookie 请求后，后续请求应保持登录
        /// </summary>
        [TestMethod]
        public void Auth_SessionCookiePersistence()
        {
            using var session = new Session(preset: HcPresets.Chrome146);

            // 第一次请求，带完整 Cookie
            var firstResponse = session.Get(TargetUrl, headers: BuildHeaders());
            Assert.AreEqual(200, firstResponse.StatusCode);

            var cookies = session.GetCookiesDetailed();
            Console.WriteLine($"Session 中的 Cookie 数量: {cookies.Count}");
            foreach (var cookie in cookies)
            {
                Console.WriteLine($"  {cookie.Name} = {cookie.Value[..Math.Min(cookie.Value.Length, 30)]}...");
            }

            // 第二次请求，不带 Cookie header，依赖 Session 持久化
            var headersNoCookie = BuildHeaders("");
            headersNoCookie.Remove("Cookie");

            var secondResponse = session.Get(TargetUrl, headers: headersNoCookie);
            Assert.AreEqual(200, secondResponse.StatusCode);

            var isLoggedIn = secondResponse.Text.Contains(LoggedInKeyword);
            Console.WriteLine($"Session 持久化后登录状态: {isLoggedIn}");
        }

        #endregion

        #region 反向验证

        /// <summary>
        /// 无 Cookie 访问应处于未登录状态
        /// </summary>
        [TestMethod]
        [DataRow(HcPresets.Chrome146)]
        [DataRow(HcPresets.Firefox133)]
        [DataRow(HcPresets.Safari18)]
        public void Access_WithoutCookie_ShouldNotBeLoggedIn(string preset)
        {
            using var session = new Session(preset: preset);
            var headers = BuildHeaders("");
            headers.Remove("Cookie");

            var response = session.Get(TargetUrl, headers: headers);

            Console.WriteLine($"[NoCookie] Status: {response.StatusCode}, Protocol: {response.Protocol}");
            Assert.AreEqual(200, response.StatusCode, "无 Cookie 请求也应返回 200（未登录页面）");

            var isLoggedIn = response.Text.Contains(LoggedInKeyword);
            Assert.IsFalse(isLoggedIn, "无 Cookie 时不应处于登录状态");
        }

        #endregion

        #region Helper

        private static void AssertLoggedIn(string preset, string label)
        {
            using var session = new Session(preset: preset);
            var response = session.Get(TargetUrl, headers: BuildHeaders());

            Console.WriteLine($"[{label}] Status: {response.StatusCode}, Protocol: {response.Protocol}");
            Assert.AreEqual(200, response.StatusCode, $"[{label}] 请求失败，状态码: {response.StatusCode}");

            var isLoggedIn = response.Text.Contains(LoggedInKeyword);
            Console.WriteLine($"[{label}] 登录状态: {isLoggedIn}");

            if (!isLoggedIn)
            {
                Console.WriteLine($"[{label}] HTML 片段:\n{response.Text[..Math.Min(response.Text.Length, 500)]}");
            }

            Assert.IsTrue(isLoggedIn, $"[{label}] 未检测到登录状态，可能是 Cookie 失效或被反爬拦截");
        }

        #endregion
    }
}
