import type { ReactNode } from "react";
import type { DownloadTask } from "@/core/models/DownloadTask";

import { HeroUIProvider } from "@heroui/react";
import { fireEvent } from "@testing-library/react";
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import TaskDetailModal from "..";

import { DownloadTaskStatus, ExHentaiDownloadTaskType, ThirdPartyId } from "@/sdk/constants";

const mocks = vi.hoisted(() => ({
  tasks: [] as DownloadTask[],
  getTask: vi.fn(),
  getDefinitions: vi.fn(),
  getOptions: vi.fn(),
  dangerToast: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    downloadTask: {
      getDownloadTask: mocks.getTask,
      getAllDownloaderDefinitions: mocks.getDefinitions,
      getDownloaderOptions: mocks.getOptions,
    },
  },
}));
vi.mock("@/components/bakaui", async () => ({
  ...(await import("@heroui/react")),
  Button: (await import("@/components/bakaui/components/Button")).Button,
  Modal: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  Alert: ({ description }: { description: ReactNode }) => <div>{description}</div>,
  toast: { danger: mocks.dangerToast },
}));
vi.mock("@/stores/downloadTasks", () => ({
  useDownloadTasksStore: (selector: (state: { tasks: DownloadTask[] }) => unknown) =>
    selector({ tasks: mocks.tasks }),
}));
vi.mock("@/stores/options", () => ({
  useDownloaderGlobalOptionsStore: (selector: (state: { data: object }) => unknown) =>
    selector({ data: {} }),
  useExHentaiOptionsStore: (selector: (state: { data: object }) => unknown) =>
    selector({ data: {} }),
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("@/components/NavigateButton", () => ({ default: () => null }));
vi.mock("@/components/ThirdPartyIcon", () => ({ default: () => null }));
vi.mock("../components/FfMpegRequired", () => ({ default: () => null }));
vi.mock("../components/PageRange", () => ({ default: () => null }));
vi.mock("../components/BilibiliFavoritesSelector", () => ({ default: () => null }));
vi.mock("../components/DownloadPathSelectorField", () => ({ default: () => null }));
vi.mock("../components/IntervalField", () => ({ default: () => null }));
vi.mock("../components/CheckpointField", () => ({ default: () => null }));
vi.mock("../components/AutoRetryField", () => ({ default: () => null }));
vi.mock("../components/AllowDuplicateField", () => ({ default: () => null }));
vi.mock("../components/PreferTorrentField", () => ({ default: () => null }));
vi.mock("../components/DownloadResultsPanel", () => ({ default: () => null }));

let root: Root;
let container: HTMLDivElement;
let writeText: ReturnType<typeof vi.fn>;
let originalClipboard: PropertyDescriptor | undefined;

async function show() {
  await act(async () =>
    root.render(
      <HeroUIProvider disableAnimation>
        <TaskDetailModal id={17} onDestroyed={vi.fn()} />
      </HeroUIProvider>,
    ),
  );
}

beforeEach(() => {
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  mocks.tasks = [
    {
      id: 17,
      key: "https://exhentai.org/g/4171374/abcdef0123/",
      thirdPartyId: ThirdPartyId.ExHentai,
      type: ExHentaiDownloadTaskType.SingleWork,
      progress: 100,
      downloadStatusUpdateDt: new Date("2026-10-01T03:00:00Z"),
      status: DownloadTaskStatus.Failed,
      downloadPath: "/downloads",
      failureTimes: 1,
      autoRetry: false,
      availableActions: [],
      displayName: "Gallery",
      canStart: true,
      createdAt: "2026-09-29T01:00:00Z",
      completedAt: "2026-09-30T02:03:04Z",
    },
  ];
  mocks.getTask.mockResolvedValue({ data: mocks.tasks[0] });
  mocks.getDefinitions.mockResolvedValue({ data: [] });
  mocks.getOptions.mockResolvedValue({ data: undefined });
  originalClipboard = Object.getOwnPropertyDescriptor(navigator, "clipboard");
  writeText = vi.fn().mockResolvedValue(undefined);
  Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText } });
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  if (originalClipboard) Object.defineProperty(navigator, "clipboard", originalClipboard);
  else Reflect.deleteProperty(navigator, "clipboard");
  vi.unstubAllGlobals();
  vi.clearAllMocks();
});

describe("download task details", () => {
  it("displays the server's last successful completion independently of the current status", async () => {
    await show();

    expect(
      container.querySelector(`time[datetime="${mocks.tasks[0].completedAt}"]`),
    ).toBeInTheDocument();
    expect(
      container.querySelector(`time[datetime="${mocks.tasks[0].createdAt}"]`),
    ).toBeInTheDocument();
    expect(container).toHaveTextContent("downloader.label.completedAt");

    mocks.tasks = [{ ...mocks.tasks[0], completedAt: null, status: DownloadTaskStatus.Complete }];
    await show();
    expect(container).not.toHaveTextContent("downloader.label.completedAt");
    expect(container.querySelectorAll("time")).toHaveLength(1);
  });

  it("copies the currently edited link from the existing textarea", async () => {
    await show();
    const input = container.querySelector("textarea")!;
    const edited = "https://exhentai.org/g/4171375/abcdef0123/?p=2";

    await act(async () => {
      fireEvent.change(input, { target: { value: edited } });
    });
    const button = container.querySelector<HTMLElement>(
      '[aria-label="downloader.action.copyDownloadLink"]',
    )!;

    await act(async () => {
      button.dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true }));
    });
    expect(writeText).toHaveBeenCalledExactlyOnceWith(edited);
    expect(container.querySelector('[aria-live="polite"]')).toHaveTextContent(
      "common.state.copied",
    );
  });
});
