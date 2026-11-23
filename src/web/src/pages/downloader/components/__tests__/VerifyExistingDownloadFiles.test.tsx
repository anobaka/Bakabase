import type { ReactNode } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { CenterTask } from "../../downloadCenter";
import { VerifyExistingFilesModal } from "../VerifyExistingDownloadFiles";

import cnCloud from "@/locales/cn/pages/cloudDownload.json";
import enCloud from "@/locales/en/pages/cloudDownload.json";
import cnDownloader from "@/locales/cn/pages/downloader.json";
import enDownloader from "@/locales/en/pages/downloader.json";

const translation = vi.hoisted(() => ({ language: undefined as "cn" | "en" | undefined }));
const messages: Record<"cn" | "en", Record<string, string>> = {
  cn: { ...cnCloud, ...cnDownloader },
  en: { ...enCloud, ...enDownloader },
};

const api = vi.hoisted(() => ({ preview: vi.fn(), verify: vi.fn(), get: vi.fn(), batch: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({
  default: {
    downloadCenter: {
      previewDownloadCenterExistingFiles: api.preview,
      verifyDownloadCenterExistingFiles: api.verify,
      getDownloadCenterTask: api.get,
      batchDownloadCenterTasks: api.batch,
    },
  },
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("@/components/bakaui", async () => ({
  Modal: ({
    visible,
    children,
    footer,
  }: {
    visible: boolean;
    children: ReactNode;
    footer: ReactNode;
  }) =>
    visible && (
      <section role="dialog">
        {children}
        {footer}
      </section>
    ),
  Button: (await import("react")).forwardRef<
    HTMLButtonElement,
    {
      onPress?: () => void;
      isDisabled?: boolean;
      children: ReactNode;
    }
  >(({ onPress, isDisabled, children }, ref) => (
    <button ref={ref} disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  )),
  Spinner: () => <span />,
}));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    i18n: { language: translation.language },
    t: (key: string, vars?: Record<string, string | number>) => {
      if (!translation.language) return vars?.id ? `${key} #${vars.id}` : key;
      const value = messages[translation.language][key] ?? key;
      return value.replace(/{{(\w+)}}/g, (_, name: string) => String(vars?.[name]));
    },
  }),
}));
const task: CenterTask = {
  id: "site:63",
  category: "site",
  provider: "baidu",
  providerTaskId: "63",
  name: "Saved ZIPs",
  status: "failed",
  createdAt: "2026-10-19",
  updatedAt: "2026-10-19",
  outputDirectory: "/old",
  originKind: "workflow",
  files: [],
  availableActions: ["verifyExistingFiles"],
};
const allowed = {
  canVerify: true,
  siteTaskId: 63,
  previewToken: "reviewed-manifest",
  outputDirectory: "/original/files",
  fileCount: 12,
  totalBytes: 2048,
};
const startLabel = "downloadCenter.existingFiles.confirm";

beforeEach(() => {
  vi.resetAllMocks();
  translation.language = undefined;
  api.preview.mockResolvedValue({ code: 0, data: allowed });
  api.get.mockResolvedValue({ code: 0, data: { stage: "VerifyingSource" } });
});
afterEach(cleanup);

describe("verify an existing download", () => {
  it("reviews the native location and only submits its exact token on confirmation, while showing the real verification stage", async () => {
    let finish!: (value: unknown) => void;
    api.verify.mockImplementation(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    render(<VerifyExistingFilesModal task={task} />);
    expect(await screen.findByText("/original/files")).toBeInTheDocument();
    expect(screen.getByText("12")).toBeInTheDocument();
    expect(screen.getByText("2.0 KiB")).toBeInTheDocument();
    expect(api.preview).toHaveBeenCalledTimes(1);
    expect(api.preview).toHaveBeenCalledWith(task.id, { showErrorToast: false });
    expect(api.verify).not.toHaveBeenCalled();
    const button = screen.getByRole("button", { name: startLabel });
    button.focus();
    fireEvent.click(button);
    fireEvent.click(button);
    expect(api.verify).toHaveBeenCalledTimes(1);
    expect(api.verify).toHaveBeenCalledWith(
      task.id,
      { previewToken: allowed.previewToken },
      { showErrorToast: false },
    );
    expect(await screen.findByText("downloadCenter.stage.VerifyingSource")).toBeInTheDocument();
    expect(api.batch).not.toHaveBeenCalled();
    await act(async () => finish({ code: 0, data: { siteTaskId: 63, downloadResultId: 102 } }));
    expect(screen.getByRole("status")).toHaveTextContent(
      "downloadCenter.existingFiles.registered #102",
    );
    expect(screen.queryByRole("button", { name: startLabel })).toBeNull();
    expect(screen.getByRole("button", { name: "common.action.close" })).toHaveFocus();
  });
  it.each([
    { ...allowed, canVerify: false, reasonCode: "PROVIDER_CHECK_UNAVAILABLE" },
    { ...allowed, previewToken: undefined },
  ])("does not enable a verification without reviewable evidence", async (data) => {
    api.preview.mockResolvedValue({ code: 0, data });
    render(<VerifyExistingFilesModal task={task} />);
    await screen.findByText("/original/files");
    expect(screen.queryByRole("button", { name: startLabel })).toBeNull();
    if (!data.canVerify)
      expect(screen.getByRole("alert")).toHaveTextContent("PROVIDER_CHECK_UNAVAILABLE");
    expect(api.verify).not.toHaveBeenCalled();
  });
  it.each([
    { code: 409, message: "DOWNLOAD_CHANGED" },
    { code: 0, data: { siteTaskId: 700, downloadResultId: 102 } },
  ])("keeps unsuccessful or mismatched results available for review", async (reply) => {
    api.verify.mockResolvedValue(reply);
    render(<VerifyExistingFilesModal task={task} />);
    fireEvent.click(await screen.findByRole("button", { name: startLabel }));
    await screen.findByRole("alert");
    expect(screen.queryByText(/existingFiles.registered/)).toBeNull();
    expect(screen.queryByRole("button", { name: startLabel })).toBeNull();
    expect(api.batch).not.toHaveBeenCalled();
  });
  const lastFailure = {
    reasonCode: "BAIDU_EXISTING_CHECKSUM_UNAVAILABLE",
    checkedAt: "2026-11-22T04:32:01Z",
    location: { fileNumber: 7, fileCount: 19 },
  };
  it.each([
    ["cn", "上次核验", "第 7 个文件，共 19 个", "重新核验文件"],
    ["en", "Last verification", "File 7 of 19", "Verify files again"],
  ] as const)(
    "opens a previous %s report without reading files and clears it after a successful retry",
    async (lang, heading, position, retry) => {
      translation.language = lang;
      api.preview.mockResolvedValue({
        code: 0,
        data: { ...allowed, verificationProblem: lastFailure },
      });
      api.verify.mockResolvedValue({ code: 0, data: { siteTaskId: 63, downloadResultId: 102 } });
      render(<VerifyExistingFilesModal task={task} />);
      expect(await screen.findByRole("heading", { name: heading })).toBeInTheDocument();
      expect(screen.getByText(position)).toBeInTheDocument();
      expect(
        screen.getByText(
          messages[lang]["cloudDownload.problemCode.BAIDU_EXISTING_CHECKSUM_UNAVAILABLE"],
        ),
      ).toBeInTheDocument();
      const time = screen.getByRole("dialog").querySelector("time");
      expect(time).toHaveAttribute("datetime", lastFailure.checkedAt);
      expect(time).toHaveTextContent(
        new Date(lastFailure.checkedAt).toLocaleString(lang === "cn" ? "zh-CN" : "en"),
      );
      expect(api.verify).not.toHaveBeenCalled();
      expect(api.get).not.toHaveBeenCalled();
      fireEvent.click(screen.getByRole("button", { name: retry }));
      await screen.findByRole("status");
      await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent("#102"));
      expect(screen.queryByRole("heading", { name: heading })).toBeNull();
      expect(screen.queryByText(position)).toBeNull();
      expect(api.verify).toHaveBeenCalledTimes(1);
    },
  );

  it.each([
    [
      "cn",
      "上次核验",
      "第 7 个文件，共 19 个",
      "首个不匹配的分块为第 6 块（33,554,432 字节）。共 15 块，其中 3 块校验不符。",
    ],
    [
      "en",
      "Last verification",
      "File 7 of 19",
      "The first mismatched block was 6 (33,554,432 bytes). 3 of 15 blocks failed verification.",
    ],
  ] as const)(
    "shows a failed %s verification receipt for this task even without a result ID",
    async (lang, heading, position, block) => {
      translation.language = lang;
      const failure = {
        ...lastFailure,
        reasonCode: "BAIDU_DOWNLOAD_CHECKSUM_MISMATCH",
        location: {
          ...lastFailure.location,
          checksumMismatch: {
            blockCount: 15,
            mismatchedBlockCount: 3,
            firstBlockNumber: 6,
            firstBlockBytes: 33554432,
          },
        },
      };
      api.verify.mockResolvedValue({
        code: 400,
        data: { siteTaskId: 63, downloadResultId: null, verificationProblem: failure },
      });
      render(<VerifyExistingFilesModal task={task} />);
      fireEvent.click(await screen.findByRole("button", { name: messages[lang][startLabel] }));
      const alert = await screen.findByRole("alert");
      expect(alert).toHaveTextContent(heading);
      expect(alert).toHaveTextContent(
        messages[lang]["cloudDownload.problemCode.BAIDU_DOWNLOAD_CHECKSUM_MISMATCH"],
      );
      expect(alert).toHaveTextContent(position);
      expect(alert).toHaveTextContent(block);
      expect(screen.queryByRole("status")).toBeNull();
      expect(api.verify).toHaveBeenCalledTimes(1);
    },
  );

  it("refreshes eligibility after a failed confirmation and removes that receipt on success", async () => {
    translation.language = "en";
    api.verify
      .mockResolvedValueOnce({
        code: 400,
        data: { siteTaskId: 63, verificationProblem: lastFailure },
      })
      .mockResolvedValueOnce({ code: 0, data: { siteTaskId: 63, downloadResultId: 103 } });
    api.preview
      .mockResolvedValueOnce({ code: 0, data: allowed })
      .mockResolvedValueOnce({
        code: 0,
        data: { ...allowed, previewToken: "latest", verificationProblem: lastFailure },
      });
    render(<VerifyExistingFilesModal task={task} />);
    fireEvent.click(await screen.findByRole("button", { name: "Verify files" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Last verification");
    fireEvent.click(screen.getByRole("button", { name: "Refresh preview" }));
    const retry = await screen.findByRole("button", { name: "Verify files again" });
    expect(api.verify).toHaveBeenCalledTimes(1);
    fireEvent.click(retry);
    await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent("#103"));
    expect(screen.queryByText("Last verification")).toBeNull();
    expect(api.verify).toHaveBeenLastCalledWith(
      task.id,
      { previewToken: "latest" },
      { showErrorToast: false },
    );
  });

  it("ignores verification details belonging to a different site task", async () => {
    api.verify.mockResolvedValue({
      code: 400,
      data: { siteTaskId: 64, downloadResultId: null, verificationProblem: lastFailure },
    });
    render(<VerifyExistingFilesModal task={task} />);
    fireEvent.click(await screen.findByRole("button", { name: startLabel }));
    await screen.findByRole("alert");
    expect(screen.queryByText("downloadCenter.existingFiles.lastVerification")).toBeNull();
    expect(screen.queryByText(lastFailure.reasonCode)).toBeNull();
    expect(screen.queryByRole("status")).toBeNull();
  });

  it("closes a review without launching or restarting the download", async () => {
    render(<VerifyExistingFilesModal task={task} />);
    await screen.findByRole("button", { name: startLabel });
    fireEvent.click(screen.getByRole("button", { name: "common.action.close" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(api.verify).not.toHaveBeenCalled();
    expect(api.batch).not.toHaveBeenCalled();
  });
  it("requires a new explicit confirmation after an expired preview", async () => {
    api.verify
      .mockResolvedValueOnce({ code: 409, message: "RECOVERY_PREVIEW_EXPIRED" })
      .mockResolvedValueOnce({ code: 0, data: { siteTaskId: 63, downloadResultId: 102 } });
    api.preview
      .mockResolvedValueOnce({ code: 0, data: allowed })
      .mockResolvedValueOnce({ code: 0, data: { ...allowed, previewToken: "fresh-proof" } });
    render(<VerifyExistingFilesModal task={task} />);
    fireEvent.click(await screen.findByRole("button", { name: startLabel }));
    fireEvent.click(
      await screen.findByRole("button", { name: "downloadCenter.existingFiles.checkAgain" }),
    );
    const button = await screen.findByRole("button", { name: startLabel });
    expect(api.verify).toHaveBeenCalledTimes(1);
    fireEvent.click(button);
    await screen.findByText(/existingFiles.registered/);
    expect(api.verify).toHaveBeenLastCalledWith(
      task.id,
      { previewToken: "fresh-proof" },
      { showErrorToast: false },
    );
    expect(api.batch).not.toHaveBeenCalled();
  });
});
