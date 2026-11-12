import { createInstance } from "i18next";
import { beforeEach, expect, it } from "vitest";
import cn from "@/locales/cn/pages/cloudDownload.json";
import en from "@/locales/en/pages/cloudDownload.json";
import {
  downloadProblemLabel,
  downloadProblemSummary,
  localStorageProblemCode,
} from "../problemLabels";

const i18n = createInstance();
beforeEach(async () => {
  await i18n.init({
    lng: "cn",
    resources: { cn: { translation: cn }, en: { translation: en } },
    parseMissingKeyHandler: (key) => key,
    interpolation: { escapeValue: false },
  });
});

it.each([
  "BAIDU_SHARE_VERIFICATION_FAILED",
  "Failure: BAIDU_SHARE_VERIFICATION_FAILED",
  "wrapped (BAIDU_SHARE_VERIFICATION_FAILED): safe diagnostic",
])("localizes a known bare or wrapped bridge code: %s", (message) => {
  expect(downloadProblemLabel(message, i18n.t)).toBe(
    cn["cloudDownload.problemCode.BAIDU_SHARE_VERIFICATION_FAILED"],
  );
  expect(downloadProblemSummary(message, i18n.t)).toContain("不一定代表提取码错误");
});
it("preserves unknown diagnostics rather than assigning a known category", () => {
  const message = "Unknown failure: SOMETHING_UNRECOGNIZED";
  expect(downloadProblemSummary(message, i18n.t)).toBeUndefined();
  expect(downloadProblemLabel(message, i18n.t)).toBe(message);
});
it.each([
  "LOCAL_STORAGE_FULL",
  "LOCAL_STORAGE_RESERVE_LOW",
  "LOCAL_STORAGE_WRITE_FAILED",
  "LOCAL_STORAGE_ACCESS_DENIED",
  "LOCAL_STORAGE_UNAVAILABLE",
  "BAIDU_OUTPUT_DIRECTORY_FAILED",
  "BAIDU_DOWNLOAD_PARTIAL_UNVERIFIED",
  "BAIDU_DOWNLOAD_CHECKPOINT_INVALID",
  "BAIDU_OUTPUT_UNEXPECTED_FILE",
  "GOOGLE_DOWNLOAD_QUOTA_EXCEEDED",
  "GOOGLE_DRIVE_SHARE_UNAVAILABLE_OR_ACCESS_REQUIRED",
  "GOFILE_SHARE_NOT_FOUND",
  "GOFILE_ACCESS_REQUIRED",
  "CLOUD_NETWORK_ERROR",
  "CLOUD_RATE_LIMITED",
  "CLOUD_ACCESS_REQUIRED",
  "CLOUD_TOOL_FAILED",
  "BAIDU_ACCESS_CODE_REJECTED",
  "BAIDU_SHARE_CANCELLED",
  "BAIDU_SHARE_EXPIRED",
  "BAIDU_SHARE_UNAVAILABLE",
  "BAIDU_SHARE_PAGE_INVALID",
  "BAIDU_SHARE_ACCESS_CODE_REJECTED",
  "BAIDU_SHARE_VERIFICATION_REQUIRED",
  "BAIDU_SHARE_RESPONSE_INVALID",
  "BAIDU_SHARE_NETWORK_ERROR",
  "BAIDU_OUTPUT_FILE_INVALID",
  "BAIDU_OUTPUT_FILE_READ_FAILED",
  "BAIDU_LOCAL_FILE_CHANGED",
  "BAIDU_DOWNLOAD_CHECKSUM_MISMATCH",
  "MEGA_DEPENDENCY_REQUIRED",
  "MEGA_PROFILE_CONFLICT",
  "MEGA_SERVER_REVIEW_REQUIRED",
  "MEGA_ISOLATED_RUNTIME_REQUIRED",
  "MEGA_PUBLIC_ACCOUNT_NOT_REQUIRED",
  "MEGA_SHARE_INVALID",
  "MEGA_REQUEST_INVALID",
  "MEGA_OPERATION_CHANGED",
  "MEGA_DOWNLOAD_REVIEW_REQUIRED",
  "MEGA_SERVER_UNAVAILABLE",
  "MEGA_EMPTY_RESULT",
  "MEGA_TRANSFER_QUOTA",
  "MEGA_DECRYPTION_FAILED",
  "MEGA_SHARE_UNAVAILABLE",
  "MEGA_ACCESS_REQUIRED",
  "MEGA_NETWORK_ERROR",
  "MEGA_DOWNLOAD_FAILED",
  "MEGA_OUTPUT_INVALID",
  "PIKPAK_ACCOUNT_REQUIRED",
  "PIKPAK_ACCOUNT_SCOPE_REQUIRED",
  "PIKPAK_PERMISSION_REQUIRED",
  "PIKPAK_STORAGE_QUOTA_EXCEEDED",
  "PIKPAK_TRANSFER_QUOTA_EXCEEDED",
  "PIKPAK_RESTORE_REVIEW_REQUIRED",
  "PIKPAK_PARTIAL_DOWNLOAD_REVIEW_REQUIRED",
  "TERABOX_DOWNLOAD_IDENTITY_REJECTED",
  "TERABOX_DOWNLOAD_ACCESS_DENIED",
])("has usable Chinese and English summaries for %s", async (code) => {
  expect(downloadProblemSummary(code, i18n.t)).toBeTruthy();
  await i18n.changeLanguage("en");
  expect(downloadProblemSummary(code, i18n.t)).toBeTruthy();
  expect(downloadProblemLabel(code, i18n.t)).not.toContain("cloudDownload.problemCode.");
});

it("prefers a structured local cause over the bridge wrapper without inferring full disk from legacy errors", () => {
  expect(
    downloadProblemSummary("BAIDU_OUTPUT_DIRECTORY_FAILED: LOCAL_STORAGE_ACCESS_DENIED", i18n.t),
  ).toBe(cn["cloudDownload.problemCode.LOCAL_STORAGE_ACCESS_DENIED"]);
  expect(downloadProblemLabel("BAIDU_OUTPUT_DIRECTORY_FAILED", i18n.t)).toContain(
    "不能据此判定网盘失效或磁盘已满",
  );
  expect(localStorageProblemCode("PIKPAK_STORAGE_QUOTA_EXCEEDED")).toBeUndefined();
  expect(localStorageProblemCode("LOCAL_STORAGE_UNKNOWN")).toBeUndefined();
  expect(localStorageProblemCode("no space left on device")).toBeUndefined();
});

it.each([
  "BAIDU_DOWNLOAD_PARTIAL_UNVERIFIED",
  "BAIDU_DOWNLOAD_CHECKPOINT_INVALID",
  "BAIDU_OUTPUT_UNEXPECTED_FILE",
])(
  "preserves uncertain local content for review rather than suggesting share expiry: %s",
  (code) => {
    const label = downloadProblemLabel(code, i18n.t);
    expect(label).toContain("保留");
    expect(label).toContain("核对");
    expect(label).not.toContain("分享已过期");
    expect(localStorageProblemCode(code)).toBeUndefined();
  },
);

it("distinguishes a cancelled share from rejected access credentials and unknown failures", async () => {
  const cancelled = "CloudProviderException: BAIDU_SHARE_CANCELLED";
  expect(downloadProblemLabel(cancelled, i18n.t)).toContain("新的有效分享链接");
  expect(downloadProblemLabel(cancelled, i18n.t)).not.toContain("提取码");
  expect(downloadProblemLabel("BAIDU_SHARE_ACCESS_CODE_REJECTED", i18n.t)).toContain("提取码");
  expect(downloadProblemLabel("BAIDU_SHARE_UNAVAILABLE", i18n.t)).toContain("尚未确认具体原因");
  await i18n.changeLanguage("en");
  expect(downloadProblemLabel(cancelled, i18n.t)).toContain("new valid share link");
  expect(downloadProblemLabel(cancelled, i18n.t)).not.toContain("access code");
});

it("does not reinterpret a historical page URL as a new stable classification", () => {
  const diagnostic = "Unable to read https://pan.baidu.com/wap/error?errortype=1";
  expect(downloadProblemSummary(diagnostic, i18n.t)).toBeUndefined();
  expect(downloadProblemLabel(diagnostic, i18n.t)).toBe(diagnostic);
});

it("shows reviewed download-stage facts without guessing that a browsable share expired", async () => {
  const identity = downloadProblemLabel("TERABOX_DOWNLOAD_IDENTITY_REJECTED", i18n.t);
  expect(identity).toContain("HTTP 403");
  expect(identity).toContain("31045");
  expect(identity).toContain("user not exists");
  expect(identity).toContain("不代表分享失效或账号不存在");
  const denied = downloadProblemLabel("TERABOX_DOWNLOAD_ACCESS_DENIED", i18n.t);
  expect(denied).toContain("HTTP 403");
  expect(denied).not.toContain("31045");
  await i18n.changeLanguage("en");
  expect(downloadProblemLabel("TERABOX_DOWNLOAD_IDENTITY_REJECTED", i18n.t)).toContain(
    "31045: user not exists",
  );
  expect(downloadProblemLabel("TERABOX_DOWNLOAD_ACCESS_DENIED", i18n.t)).not.toContain(
    "user not exists",
  );
});

it("explains that a checksum mismatch has not been delivered and preserves files without suggesting different access credentials", async () => {
  const diagnostic = "BaiduPCS-Go: BAIDU_DOWNLOAD_CHECKSUM_MISMATCH";
  const chinese = downloadProblemLabel(diagnostic, i18n.t);
  expect(chinese).toBe(cn["cloudDownload.problemCode.BAIDU_DOWNLOAD_CHECKSUM_MISMATCH"]);
  expect(chinese).toContain("未交付");
  expect(chinese).toContain("保留");
  expect(chinese).not.toContain("提取码");
  await i18n.changeLanguage("en");
  const english = downloadProblemLabel(diagnostic, i18n.t);
  expect(english).toBe(en["cloudDownload.problemCode.BAIDU_DOWNLOAD_CHECKSUM_MISMATCH"]);
  expect(english).toMatch(/not.*deliver/i);
  expect(english).toMatch(/preserv|retain|kept|keep/i);
  expect(english).not.toMatch(/access[ -]code/i);
});
