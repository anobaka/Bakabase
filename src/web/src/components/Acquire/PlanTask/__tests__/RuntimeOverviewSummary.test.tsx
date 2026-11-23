import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeAll, expect, it, vi } from "vitest";
import { createInstance } from "i18next";
import { useState } from "react";
import RuntimeOverviewSummary from "../RuntimeOverviewSummary";
import type { RuntimeOverview } from "../runtimeOverview";
import acquisition from "@/locales/cn/pages/acquisition.json";
import directories from "@/locales/cn/pages/acquisitionDirectories.json";
import dateTime from "@/locales/cn/datetime.json";
import cloud from "@/locales/cn/pages/cloudDownload.json";
import downloader from "@/locales/cn/pages/downloader.json";
import workflow from "@/locales/cn/pages/workflow.json";
import english from "@/locales/en/pages/acquisition.json";
import { parseServerTime } from "@/core/serverTime";

const { copy } = vi.hoisted(() => ({ copy: vi.fn() }));
const i18n = createInstance();
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: i18n.t }) }));
vi.mock("@/components/Workflow/displayNames", () => ({
  activityDisplayName: (_t: unknown, kind: string, fallback?: string) =>
    kind === "custom.analyze" ? fallback ?? kind : "下载来源",
}));
vi.mock("@/components/ExternalLink", () => ({
  default: ({ href, children }: any) => <a href={href}>{children}</a>,
}));
vi.mock("@/core/clipboard", () => ({ copyTextToClipboard: copy }));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, ...props }: any) => (
    <button aria-label={props["aria-label"]} onClick={onPress}>
      {children}
    </button>
  ),
  Chip: ({ children, color }: any) => <span data-color={color}>{children}</span>,
  Tooltip: ({ children }: any) => children,
  Progress: ({ value, ...props }: any) => (
    <div role="progressbar" aria-label={props["aria-label"]} aria-valuenow={value} />
  ),
  Popover: ({ trigger, children }: any) => {
    const [open, setOpen] = useState(false);
    return (
      <div>
        <div onClick={() => setOpen(!open)}>{trigger}</div>
        {open && <div role="dialog">{children}</div>}
      </div>
    );
  },
  Accordion: () => null,
  AccordionItem: () => null,
}));
beforeAll(async () => {
  await i18n.init({
    lng: "cn",
    fallbackLng: false,
    keySeparator: false,
    interpolation: { escapeValue: false },
    resources: { cn: { translation: { ...acquisition, ...directories, ...cloud, ...downloader, ...workflow, ...dateTime,
      "custom.capability.name": "检查媒体元信息" } } },
  });
});
afterEach(cleanup);
const overview = (patch: Partial<RuntimeOverview> = {}): RuntimeOverview => ({
  taskId: 969,
  planRevision: 4,
  workflowRunId: 11,
  currentStepIndex: 2,
  stepCount: 5,
  currentNodeKind: "download-source",
  downloadCount: 0,
  activeDownloadCount: 0,
  completedDownloadCount: 0,
  failedDownloadCount: 0,
  observedAt: "2026-10-25 09:00:00",
  sources: [],
  attentionTags: [],
  bytesPartial: false,
  sourcesTruncated: false,
  downloadsCompletedAwaitingWorkflow: false,
  canDiagnoseDownloadContinuation: false,
  ...patch,
});

it("shows the concrete AI observation without truncating it or interpreting markup, followed by the generic guidance", () => {
  const message = 'Requested release RJ01006490 differs from RJ01301819.zip.\n<img src=x onerror="alert(1)">';
  const view = render(<RuntimeOverviewSummary overview={overview({ currentNodeKind: "fs.assessDelivery",
    workflowStatus: "waiting", problemCode: "FILE_DELIVERY_RESULT_UNCERTAIN" })} problemDetail={message} />);
  const explanation = screen.getByText(/Requested release RJ01006490/);
  expect(explanation).toBeVisible();
  expect(explanation).not.toHaveClass("line-clamp-2");
  expect(view.container.querySelector("img")).toBeNull();
  expect(screen.getByText(cloud["cloudDownload.problemCode.FILE_DELIVERY_RESULT_UNCERTAIN"])).toBeVisible();
  view.rerender(<RuntimeOverviewSummary overview={overview({ problemCode: "FILE_DELIVERY_RESULT_UNCERTAIN" })}
    problemDetail={cloud["cloudDownload.problemCode.FILE_DELIVERY_RESULT_UNCERTAIN"]} />);
  expect(screen.getAllByText(cloud["cloudDownload.problemCode.FILE_DELIVERY_RESULT_UNCERTAIN"])).toHaveLength(1);
});

it("shows the structured password wait and replaces it with the actual current-directory failure on a failed attempt", () => {
  const view = render(<RuntimeOverviewSummary overview={overview({ currentNodeKind: "fs.assessDelivery",
    workflowStatus: "waiting", problemCode: "PasswordUnknown" })} />);
  expect(screen.getByText(workflow["workflow.metadata.warning.PasswordUnknown"])).toBeVisible();
  view.rerender(<RuntimeOverviewSummary overview={overview({ currentNodeKind: "fs.assessDelivery",
    workflowStatus: "failed", problemCode: "PLAN_CURRENT_DIRECTORY_CHANGED", problem: "PLAN_CURRENT_DIRECTORY_CHANGED" })} />);
  expect(screen.getByText(acquisition["acquisition.plan.replan.error.PLAN_CURRENT_DIRECTORY_CHANGED"])).toBeVisible();
  expect(screen.queryByText(workflow["workflow.metadata.warning.PasswordUnknown"])).toBeNull();
  expect(screen.queryByText("PLAN_CURRENT_DIRECTORY_CHANGED")).toBeNull();
});
it.each([
  ["PROCESSING_CONFIRMATION_REQUIRED", "acquisition.plan.issue.PROCESSING_CONFIRMATION_REQUIRED"],
  ["PROCESSING_STEPS_MISSING", "acquisition.plan.issue.PROCESSING_STEPS_MISSING"],
])("uses existing plan issue translations for runtime problem %s", (code, key) => {
  render(<RuntimeOverviewSummary overview={overview({ currentNodeKind: "fs.assessDelivery",
    workflowStatus: "waiting", problemCode: code, problem: code })} />);
  expect(screen.getByText(acquisition[key as keyof typeof acquisition])).toBeVisible();
  expect(screen.queryByText(code)).toBeNull();
});
it.each([
  ["STAGE_DIRECTORY_COMMIT_PENDING", "acquisition.directories.error.STAGE_DIRECTORY_COMMIT_PENDING"],
  ["STAGE_COMMIT_TARGET_CONFLICT", "acquisition.plan.replan.error.STAGE_COMMIT_TARGET_CONFLICT"],
])("shows the actionable directory explanation for %s", (code, key) => {
  render(<RuntimeOverviewSummary overview={overview({ currentNodeKind: "acquisition.plan.process",
    workflowStatus: "failed", problemCode: code, problem: code })} />);
  expect(screen.getByText(directories[key as keyof typeof directories])).toBeVisible();
  expect(screen.queryByText(code)).toBeNull();
});
it.each([
  ["failed", "acquisition.plan.process", "LOCAL_STORAGE_FULL"],
  ["failed", "fs.assessDelivery", "LOCAL_STORAGE_RESERVE_LOW"],
  ["waiting", "download.awaitCompletion", "LOCAL_STORAGE_RESERVE_LOW"],
  ["waiting", "fs.publishArtifacts", "LOCAL_STORAGE_UNAVAILABLE"],
])("shows the specific storage reason directly for a %s %s attempt (%s)", (workflowStatus, currentNodeKind, code) => {
  render(<RuntimeOverviewSummary overview={overview({ workflowStatus, currentNodeKind,
    problemCode: "StorageUnavailable", problemReasonCode: code,
    problem: "Specific storage failure from the current execution", attentionTags: ["localStorage"],
  })} showSources={false} />);
  expect(screen.getByText(cloud[`cloudDownload.problemCode.${code}` as keyof typeof cloud])).toBeVisible();
  expect(screen.queryByText(workflow["workflow.metadata.warning.StorageUnavailable"])).toBeNull();
  expect(screen.queryByText("Specific storage failure from the current execution")).toBeNull();
  expect(screen.queryByRole("dialog")).toBeNull();
});
it.each(["CLOUD_RANGE_UNCONFIRMED", "CLOUD_PARTIAL_IDENTITY_UNVERIFIED", "CLOUD_DOWNLOAD_CHECKSUM_MISMATCH"])(
  "explains preserved partial downloads for %s in the task itself", (code) => {
    render(<RuntimeOverviewSummary overview={overview({ workflowStatus: "waiting", currentNodeKind: "download.awaitCompletion",
      problemCode: code, problem: code })} showSources={false} />);
    expect(screen.getByText(cloud[`cloudDownload.problemCode.${code}` as keyof typeof cloud])).toBeVisible();
    expect(screen.queryByText(code)).toBeNull();
  },
);
it("shows an uncertain Baidu failure inline and exposes URL, code and historical evidence without checking the source", () => {
  render(
    <RuntimeOverviewSummary
      overview={overview({
        problemCode: "BAIDU_SHARE_UNAVAILABLE",
        sources: [
          {
            partId: "p",
            sourceId: "s",
            url: "https://pan.baidu.com/s/example",
            accessCode: "abcd",
            providerId: "baidu",
            selected: true,
            status: "unknown",
            reasonCode: "BAIDU_SHARE_UNAVAILABLE",
            evidenceKind: "workflow",
            isHistorical: true,
            observedAt: "2026-10-24 09:00:00",
          },
        ],
      })}
    />,
  );
  expect(screen.getByText(/百度分享当前无法访问，是否失效尚未确认/)).toBeVisible();
  expect(screen.getByText("第 3 / 5 步")).toBeVisible();
  expect(screen.queryByRole("dialog")).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "来源信息（1）" }));
  expect(screen.getByRole("link")).toHaveAttribute("href", "https://pan.baidu.com/s/example");
  expect(screen.getByText("abcd")).toBeVisible();
  expect(screen.getByText(/这是此前的反馈/)).toBeVisible();
  expect(
    screen.getByText(`记录时间：${parseServerTime("2026-10-24 09:00:00")!.toLocaleString()}`),
  ).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "复制提取码或访问密码" }));
  expect(copy).toHaveBeenCalledWith("abcd");
});
it("renders only measured download progress, localizes its stage and does not invent a total or ETA", () => {
  const view = render(
    <RuntimeOverviewSummary
      overview={overview({
        progressScope: "download",
        progress: 23.7,
        completedBytes: 1024,
        speedBytesPerSecond: 512,
        stage: "Verifying",
        downloadCount: 1,
        activeDownloadCount: 1,
      })}
    />,
  );
  expect(screen.getByRole("progressbar", { name: "下载阶段进度" })).toHaveAttribute(
    "aria-valuenow",
    "23.7",
  );
  expect(screen.getByText("下载阶段 24%")).toBeVisible();
  expect(screen.getByText("正在校验文件")).toBeVisible();
  expect(screen.getByText(/已下载 1/)).toBeVisible();
  expect(screen.getByText(/当前下载 512.*\/s/)).toBeVisible();
  expect(screen.queryByText(/ETA|剩余|预计/)).toBeNull();
  view.rerender(
    <RuntimeOverviewSummary overview={overview({ progressScope: "workflow", progress: 80 })} />,
  );
  expect(screen.queryByRole("progressbar")).toBeNull();
  expect(screen.queryByText(/80%|已下载/)).toBeNull();
});
it("shows measured speed and the download-stage estimate beside the percentage", () => {
  const live = overview({
    progressScope: "download", progress: 50, workflowStatus: "running",
    downloadCount: 2, activeDownloadCount: 2, speedBytesPerSecond: 1572864,
    estimatedRemainingSeconds: 65,
  });
  const view = render(<RuntimeOverviewSummary overview={live} />);
  const row = screen.getByRole("progressbar", { name: "下载阶段进度" }).parentElement;
  expect(row).toContainElement(screen.getByText("下载阶段 50%"));
  expect(row).toContainElement(screen.getByText("当前下载 1.5 MiB/s"));
  expect(row).toContainElement(screen.getByText("预计剩余 1分 5秒"));

  view.rerender(<RuntimeOverviewSummary overview={{ ...live, speedBytesPerSecond: 0, estimatedRemainingSeconds: undefined }} />);
  expect(screen.getByText("当前下载 0 B/s")).toBeVisible();
  expect(screen.queryByText(/预计剩余/)).toBeNull();

  view.rerender(<RuntimeOverviewSummary overview={{ ...live, progressScope: "archive", stage: "extractingArchive" }} />);
  expect(screen.queryByText(/当前下载|预计剩余/)).toBeNull();
  expect(screen.getByRole("progressbar", { name: "当前压缩包解压进度" })).toBeVisible();

  view.rerender(<RuntimeOverviewSummary overview={{ ...live, workflowStatus: "waiting", activeDownloadCount: 0 }} />);
  expect(screen.queryByText(/当前下载|预计剩余/)).toBeNull();
});
it("distinguishes live input preparation from the archive tool's measured extraction percentage", () => {
  const running = { currentNodeKind: "acquisition.plan.process", workflowStatus: "running" };
  const view = render(<RuntimeOverviewSummary overview={overview({ ...running,
    stage: "preparingFiles", progressScope: "processing", progress: undefined })} />);
  expect(screen.getByText("正在准备处理文件")).toBeVisible();
  expect(screen.getByRole("progressbar")).not.toHaveAttribute("aria-valuenow");
  view.rerender(<RuntimeOverviewSummary overview={overview({ ...running,
    stage: "extractingArchive", progressScope: "archive", progress: 62 })} />);
  expect(screen.getByText("当前压缩包已解压 62%")).toBeVisible();
  expect(screen.getByRole("progressbar", { name: "当前压缩包解压进度" })).toHaveAttribute("aria-valuenow", "62");
  view.rerender(<RuntimeOverviewSummary overview={overview({ ...running, workflowStatus: "waiting",
    stage: "extractingArchive", progressScope: "archive", progress: 62 })} />);
  expect(screen.queryByRole("progressbar")).toBeNull();
  expect(screen.queryByText("正在解压当前压缩包")).toBeNull();
});

it("localizes the AI queue stage without inventing a percentage", () => {
  render(<RuntimeOverviewSummary overview={overview({ currentNodeKind: "acquisition.plan.process",
    workflowStatus: "running", stage: "waitingForAi", progressScope: "processing", progress: undefined })} />);
  expect(screen.getByText("等待 AI 分析名额")).toBeVisible();
  expect(screen.getByRole("progressbar")).not.toHaveAttribute("aria-valuenow");
  expect(screen.queryByText(/%/)).toBeNull();
});

it("uses registered capability metadata and raw stage text for an unknown activity", () => {
  const view = render(<RuntimeOverviewSummary overview={overview({ currentNodeKind: "custom.analyze",
    capability: { id: "custom", displayName: "Inspect media metadata", displayNameKey: "custom.unknown.name" },
    workflowStatus: "running", stage: "Reading media tags", progressScope: "metadata", progress: 70 })} />);
  expect(screen.getByText("当前步骤：Inspect media metadata")).toBeVisible();
  expect(screen.getByText("Reading media tags")).toBeVisible();
  expect(screen.queryByRole("progressbar")).toBeNull();
  expect(view.container.textContent).not.toContain("downloadCenter.stage.");

  view.rerender(<RuntimeOverviewSummary overview={overview({ currentNodeKind: "custom.analyze",
    capability: { id: "custom", displayName: "Inspect media metadata", displayNameKey: "custom.capability.name" },
    workflowStatus: "running", stage: "Reading media tags", progressScope: "processing", progress: undefined })} />);
  expect(screen.getByText("Reading media tags")).toBeVisible();
  expect(screen.getByText("当前步骤：检查媒体元信息")).toBeVisible();
  expect(view.container.textContent).not.toContain("acquisition.runtime.fileStage.");
});

it("uses the server's generic classification for both task and source reasons", () => {
  render(
    <RuntimeOverviewSummary
      overview={overview({
        problemCode: "BAIDU_SHARE_EXPIRED",
        problemReasonCode: "SOURCE_SHARE_EXPIRED",
        sources: [
          {
            partId: "p",
            sourceId: "s",
            providerId: "baidu",
            selected: true,
            url: "https://pan.baidu.com/s/example",
            status: "confirmedUnavailable",
            reasonCode: "BAIDU_SHARE_EXPIRED",
            normalizedReasonCode: "SOURCE_SHARE_EXPIRED",
            evidenceKind: "workflow",
            isHistorical: true,
          },
        ],
      })}
    />,
  );
  expect(screen.getByText("网盘明确返回分享已过期。")).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "来源信息（1）" }));
  expect(screen.getAllByText("网盘明确返回分享已过期。")).toHaveLength(2);
  expect(screen.queryByText("BAIDU_SHARE_EXPIRED")).toBeNull();
});
it("marks partial byte counts and limited source samples rather than claiming a complete total", () => {
  render(
    <RuntimeOverviewSummary
      overview={overview({
        completedBytes: 1024,
        totalBytes: 2048,
        bytesPartial: true,
        sourcesTruncated: true,
        sources: [
          {
            partId: "p",
            sourceId: "s",
            providerId: "unknown",
            url: "https://example.test/a",
            selected: true,
            status: "available",
            evidenceKind: "download",
            isHistorical: true,
          },
        ],
      })}
    />,
  );
  expect(screen.getByText(/部分下载未报告大小/)).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "来源信息（1）" }));
  expect(screen.getByText(/仅展示部分关联来源/)).toBeVisible();
});
it("does not style expired historical unavailability as a fresh confirmed invalid link", () => {
  render(
    <RuntimeOverviewSummary
      overview={overview({
        sources: [
          {
            partId: "p",
            sourceId: "s",
            providerId: "unknown",
            url: "https://example.test/a",
            selected: true,
            status: "confirmedUnavailable",
            evidenceKind: "probe",
            expiresAt: "2020-01-01 00:00:00",
            isHistorical: false,
          },
        ],
      })}
    />,
  );
  fireEvent.click(screen.getByRole("button", { name: "来源信息（1）" }));
  expect(screen.getByText(/^历史记录/)).toHaveAttribute("data-color", "default");
});
it("has both languages for every runtime label", () => {
  for (const key of Object.keys(acquisition).filter((key) =>
    key.startsWith("acquisition.runtime."),
  )) {
    expect((english as Record<string, string>)[key], key).toBeTruthy();
  }
});

it.each([
  ["downloadsOnly", "下载已完成；此前选择了仅恢复下载，后续处理保持暂停。"],
  ["paused", "下载已完成，后续处理已暂停。"],
  ["manual", "下载已完成，后续处理等待诊断并确认继续。"],
  ["automatic", "下载已完成，工作流正在自动衔接后续处理。"],
])("shows completed downloads separately from %s workflow continuation", (mode, label) => {
  render(<RuntimeOverviewSummary overview={overview({
    currentNodeKind: "download.awaitCompletion",
    downloadCount: 13,
    completedDownloadCount: 13,
    downloadContinuation: mode,
    downloadsCompletedAwaitingWorkflow: true,
    canDiagnoseDownloadContinuation: mode !== "automatic",
    sources: [{
      partId: "p", sourceId: "s", providerId: "baidu", selected: true, url: "https://example.test/a",
      status: "confirmedUnavailable", reasonCode: "BAIDU_SHARE_UNAVAILABLE",
      evidenceKind: "workflow", isHistorical: true,
    }],
  })} />);
  expect(screen.getByText(label)).toBeVisible();
  expect(screen.getByText("下载 13 / 13 项已完成 · 0 项进行中")).toBeVisible();
  expect(screen.queryByText(/百度分享当前无法访问/)).toBeNull();
});

it("does not claim completion from the continuation preference alone", () => {
  render(<RuntimeOverviewSummary overview={overview({
    downloadCount: 13, completedDownloadCount: 12, downloadContinuation: "downloadsOnly",
  })} />);
  expect(screen.queryByText(/下载已完成/)).toBeNull();
});

it("retains a newer verification error after downloads completed", () => {
  render(<RuntimeOverviewSummary overview={overview({
    downloadCount: 13, completedDownloadCount: 13,
    downloadsCompletedAwaitingWorkflow: true, downloadContinuation: "automatic",
    problemCode: "RECOVERY_MANIFEST_CHANGED", attentionTags: ["fileProcessing"],
  })} />);
  expect(screen.getByText("下载已完成，后续处理需要先解决下方问题。")).toBeVisible();
  expect(screen.getByText("生成文件清单时检测到文件大小或修改时间变化。")).toBeVisible();
  expect(screen.queryByText("下载已完成，工作流正在自动衔接后续处理。")).toBeNull();
});

it.each([["VerifyingSource", "正在核对来源文件"], ["VerifyingLocalFiles", "正在复核本地文件"]])(
  "shows the bound verification stage %s without suggesting transfer activity", (stage, label) => {
    render(<RuntimeOverviewSummary overview={overview({ stage, workflowStatus: "running",
      progressScope: "download", progress: 99, completedBytes: 12345, downloadCount: 1, activeDownloadCount: 1,
      speedBytesPerSecond: 12345, estimatedRemainingSeconds: 90, problem: "previous transfer failure",
    })} />);
    expect(screen.getByText(label)).toBeVisible();
    expect(screen.getByRole("progressbar")).not.toHaveAttribute("aria-valuenow");
    expect(screen.queryByText(/99%|previous transfer failure|KiB/)).toBeNull();
  },
);

it.each([undefined, 11])("shows an active confirmation for run %s without stale transfer or workflow progress", run => {
  render(<RuntimeOverviewSummary overview={overview({
    workflowRunId: run, workflowStatus: run ? "waiting" : undefined,
    stage: "verifyingFiles", progressScope: "confirmation", problem: "Previous transfer failed",
    progress: 100, completedBytes: 199, totalBytes: 199, activeDownloadCount: 1, downloadCount: 1,
    downloadsCompletedAwaitingWorkflow: true, speedBytesPerSecond: 80, estimatedRemainingSeconds: 4,
  })} />);
  expect(screen.getByText("正在核验已有文件")).toBeVisible();
  expect(screen.getByRole("progressbar", { name: "已有文件核验进度" })).not.toHaveAttribute("aria-valuenow");
  expect(screen.queryByText(/Previous transfer failed|100%|预计剩余|当前下载/)).toBeNull();
});
