import { describe, expect, it } from "vitest";

import { getDownloadUrl, getLinkHealthStatus } from "../downloadLinks";

describe("download links", () => {
  it("adds a separate Baidu access code while retaining existing query values and fragments", () => {
    expect(getDownloadUrl(" https://pan.baidu.com/s/example?source=post#files ", " aBc1 ")).toBe(
      "https://pan.baidu.com/s/example?source=post&pwd=aBc1#files",
    );
    expect(getDownloadUrl("https://yun.baidu.com/s/example", "a+b")).toBe(
      "https://yun.baidu.com/s/example?pwd=a%2Bb",
    );
  });

  it.each(["own-code", ""])("preserves an existing pwd, including an empty one (%s)", (pwd) => {
    const link = `https://pan.baidu.com/s/example?pwd=${pwd}&source=post`;

    expect(getDownloadUrl(link, "different-code")).toBe(link);
  });

  it.each([
    "https://mega.nz/file/example",
    "https://pan.baidu.com.example.org/s/example",
    "https://example.org/pan.baidu.com",
    "ftp://pan.baidu.com/s/example",
    "magnet:?xt=urn:btih:example",
    "not a URL",
  ])("keeps non-Baidu or unsupported URLs unchanged: %s", (link) => {
    expect(getDownloadUrl(link, "aBc1")).toBe(link);
  });

  it("does not invent a URL or access code", () => {
    expect(getDownloadUrl(undefined, "aBc1")).toBe("");
    expect(getDownloadUrl("https://pan.baidu.com/s/example", " ")).toBe(
      "https://pan.baidu.com/s/example",
    );
  });

  it("distinguishes unsupported checking from inconclusive and completed checks", () => {
    expect(getLinkHealthStatus({ status: "unknown", reason: "unsupportedProvider" })).toBe(
      "unsupported",
    );
    expect(
      getLinkHealthStatus({ status: "unknown", reason: "megaFolderOrUnsupportedShareUrl" }),
    ).toBe("unsupported");
    expect(getLinkHealthStatus({ status: "unknown", reason: "checkTimedOut" })).toBe("unknown");
    expect(getLinkHealthStatus({ status: "available" })).toBe("available");
    expect(getLinkHealthStatus({ status: "unavailable" })).toBe("unavailable");
    expect(getLinkHealthStatus()).toBe("unknown");
  });
});
