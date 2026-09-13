import { describe, expect, it } from "vitest";

import { MAX_SOURCE_LENGTH, validateAcquisitionSource } from "../sourcePicker";

import { AcquisitionLeadKind as Kind } from "@/sdk/constants";

describe("acquisition source validation", () => {
  it.each([
    [Kind.DirectUrl, "https://files.example.com/download?id=42"],
    [Kind.DirectUrl, "http://files.example.com/work.zip"],
    [Kind.SharedPage, "https://pan.baidu.com/s/example"],
    [Kind.SharedPage, "https://www.south-plus.net/read.php?tid=42"],
    [Kind.SharedDocument, "下载地址：https://example.com/work.zip\n密码：example"],
    [Kind.Magnet, `magnet:?xt=urn:btih:${"01234567".repeat(5)}&dn=Example`],
    [Kind.Magnet, "magnet:?xt=urn:btih:ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"],
    [Kind.Magnet, `magnet:?xt=urn:btmh:1220${"a".repeat(64)}`],
  ] as const)("accepts a supported source (%s): %s", (kind, value) => {
    expect(validateAcquisitionSource(kind, value)).toBeUndefined();
  });

  it.each([
    [undefined, "https://example.com/work.zip", "chooseMethod"],
    [Kind.DirectUrl, "  ", "required"],
    [Kind.DirectUrl, "file:///srv/work.zip", "httpUrl"],
    [Kind.DirectUrl, "https:example.com/work.zip", "httpUrl"],
    [Kind.DirectUrl, "https://example.com/file name.zip", "httpUrl"],
    [Kind.DirectUrl, "https://pan.baidu.com/s/example", "sharingPage"],
    [Kind.DirectUrl, "https://www.south-plus.net/read.php?tid=42", "sharingPage"],
    [Kind.SharedPage, "javascript:alert(1)", "httpUrl"],
    [Kind.SharedDocument, "short", "textTooShort"],
    [Kind.SharedDocument, "https://example.com/work.zip", "textOnlyUrl"],
    [Kind.SharedDocument, "file:///srv/work.zip", "textOnlyUrl"],
    [Kind.Magnet, "https://example.com/work.torrent", "magnet"],
    [Kind.Magnet, "magnet:?xt=urn:btih:invalid", "magnet"],
    [Kind.Magnet, `magnet://example.com?xt=urn:btih:${"01234567".repeat(5)}`, "magnet"],
    [Kind.Magnet, "magnet:?dn=Example", "magnet"],
    [Kind.SharedDocument, "a".repeat(MAX_SOURCE_LENGTH + 1), "tooLong"],
  ] as const)("rejects incompatible input with %s / %s", (kind, value, error) => {
    expect(validateAcquisitionSource(kind, value)).toBe(error);
  });
});
