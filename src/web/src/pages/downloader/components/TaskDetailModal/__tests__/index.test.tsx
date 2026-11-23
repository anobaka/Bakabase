import type { ReactNode } from "react";
import type { DownloadTask } from "@/core/models/DownloadTask";
import type { ExHentaiDownloadOptions } from "@/pages/downloader/exHentaiDownloadMode";

import { HeroUIProvider } from "@heroui/react";
import { act, fireEvent } from "@testing-library/react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import TaskDetailModal, { SiteDownloadForm } from "..";

import {
  DownloadTaskStatus,
  ExHentaiDownloadMode,
  ExHentaiDownloadTaskType,
  ThirdPartyId,
} from "@/sdk/constants";

const mocks = vi.hoisted(() => ({
  tasks: [] as DownloadTask[],
  getTask: vi.fn(),
  getDefinitions: vi.fn(),
  getOptions: vi.fn(),
  dangerToast: vi.fn(),
  defaults: {} as ExHentaiDownloadOptions,
  modeOptions: vi.fn(),
  modeContext: vi.fn(),
  putTask: vi.fn(),
  addTask: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    downloadTask: {
      getDownloadTask: mocks.getTask,
      getAllDownloaderDefinitions: mocks.getDefinitions,
      getDownloaderOptions: mocks.getOptions,
      putDownloadTask: mocks.putTask,
      addDownloadTask: mocks.addTask,
    },
  },
}));
vi.mock("@/components/bakaui", async () => ({
  ...(await import("@heroui/react")),
  Button: (await import("@/components/bakaui/components/Button")).Button,
  Modal: ({ children, onOk, footer }: any) => (
    <div>
      {children}
      <button disabled={footer.okProps.isDisabled} onClick={() => void onOk()}>
        save-task
      </button>
    </div>
  ),
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
    selector({ data: mocks.defaults }),
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("@/components/NavigateButton", () => ({ default: () => null }));
vi.mock("@/components/ThirdPartyIcon", () => ({ default: () => null }));
vi.mock("../components/FfMpegRequired", () => ({ default: () => null }));
vi.mock("../components/PageRange", () => ({ default: () => null }));
vi.mock("../components/BilibiliFavoritesSelector", () => ({ default: () => null }));
vi.mock("../components/DownloadPathSelectorField", () => ({
  default: ({
    downloadPath,
    onChange,
  }: {
    downloadPath?: string;
    onChange: (path: string) => void;
  }) => (
    <input
      aria-label="download-directory"
      value={downloadPath || ""}
      onChange={(event) => onChange(event.target.value)}
    />
  ),
}));
vi.mock("../components/IntervalField", () => ({ default: () => null }));
vi.mock("../components/CheckpointField", () => ({ default: () => null }));
vi.mock("../components/AutoRetryField", () => ({ default: () => null }));
vi.mock("../components/AllowDuplicateField", () => ({ default: () => null }));
vi.mock("../components/ExHentaiDownloadModeField", () => ({
  default: ({
    options,
    isExistingTask,
  }: {
    options: ExHentaiDownloadOptions;
    isExistingTask: boolean;
  }) => {
    mocks.modeOptions(options);
    mocks.modeContext(isExistingTask);
    return null;
  },
}));
vi.mock("../components/DownloadResultsPanel", () => ({ default: () => null }));

let root: Root;
let container: HTMLDivElement;
let writeText: ReturnType<typeof vi.fn>;
let originalClipboard: PropertyDescriptor | undefined;

async function show(id?: number) {
  await act(async () =>
    root.render(
      <HeroUIProvider disableAnimation>
        <TaskDetailModal id={id} onDestroyed={vi.fn()} />
      </HeroUIProvider>,
    ),
  );
}

beforeEach(() => {
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  mocks.defaults = {};
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
  mocks.putTask.mockResolvedValue({ code: 0 });
  mocks.addTask.mockResolvedValue({ code: 0 });
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
  it("renders both live verification stages and clears the label on completion", async () => {
    for (const stage of ["VerifyingSource", "VerifyingLocalFiles"]) {
      mocks.tasks[0] = { ...mocks.tasks[0], fileVerification: { stage, observedAt: "2026-11-20T04:00:00Z" } };
      await show(17);
      expect(container).toHaveTextContent(`downloadCenter.stage.${stage}`);
    }
    mocks.tasks[0] = { ...mocks.tasks[0], status: DownloadTaskStatus.Complete, fileVerification: undefined };
    await show(17);
    expect(container.textContent).not.toContain("VerifyingLocalFiles");
  });

  it("displays the server's last successful completion independently of the current status", async () => {
    await show(17);

    expect(
      container.querySelector(`time[datetime="${mocks.tasks[0].completedAt}"]`),
    ).toBeInTheDocument();
    expect(
      container.querySelector(`time[datetime="${mocks.tasks[0].createdAt}"]`),
    ).toBeInTheDocument();
    expect(container).toHaveTextContent("downloader.label.completedAt");

    mocks.tasks = [{ ...mocks.tasks[0], completedAt: null, status: DownloadTaskStatus.Complete }];
    await show(17);
    expect(container).not.toHaveTextContent("downloader.label.completedAt");
    expect(container.querySelectorAll("time")).toHaveLength(1);
  });

  it("copies the currently edited link from the existing textarea", async () => {
    await show(17);
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

describe("new download task defaults", () => {
  it("creates a fixed-site task through the embedded form with the existing validation", async () => {
    const created = vi.fn();
    const state = vi.fn();
    await act(async () =>
      root.render(
        <HeroUIProvider disableAnimation>
          <SiteDownloadForm
            formId="embedded-site"
            thirdPartyId={ThirdPartyId.ExHentai}
            definitions={[
              {
                thirdPartyId: ThirdPartyId.ExHentai,
                taskType: ExHentaiDownloadTaskType.SingleWork,
                name: "Single gallery",
              },
            ]}
            onStateChange={state}
            onCreated={created}
          />
        </HeroUIProvider>,
      ),
    );
    expect(container.textContent).not.toContain("downloader.label.site");
    expect(state).toHaveBeenLastCalledWith({ canSubmit: false, busy: false });
    const type = Array.from(container.querySelectorAll("button")).find(
      (button) => button.textContent === "Single gallery",
    )!;
    await act(async () => type.click());
    await act(async () => {
      fireEvent.change(container.querySelector("textarea")!, {
        target: { value: " https://exhentai.org/g/123/token/ " },
      });
    });
    expect(state).toHaveBeenLastCalledWith({ canSubmit: false, busy: false });
    await act(async () =>
      fireEvent.change(container.querySelector('[aria-label="download-directory"]')!, {
        target: { value: "/site-output" },
      }),
    );
    expect(state).toHaveBeenLastCalledWith({ canSubmit: true, busy: false });
    await act(async () => fireEvent.submit(container.querySelector("form")!));
    expect(mocks.addTask).toHaveBeenCalledWith(
      expect.objectContaining({
        keys: ["https://exhentai.org/g/123/token/"],
        downloadPath: "/site-output",
        thirdPartyId: ThirdPartyId.ExHentai,
        type: ExHentaiDownloadTaskType.SingleWork,
      }),
    );
    expect(created).toHaveBeenCalledOnce();
    expect(mocks.getDefinitions).not.toHaveBeenCalled();
  });

  it("keeps edits on the existing task API after embedding the shared form", async () => {
    await show(17);
    await act(async () =>
      fireEvent.change(container.querySelector('[aria-label="download-directory"]')!, {
        target: { value: "/edited-directory" },
      }),
    );
    const save = Array.from(container.querySelectorAll("button")).find(
      (button) => button.textContent === "save-task",
    )!;
    await act(async () => save.click());
    expect(mocks.putTask).toHaveBeenCalledWith(
      17,
      expect.objectContaining({
        downloadPath: "/edited-directory",
        keys: ["https://exhentai.org/g/4171374/abcdef0123/"],
        thirdPartyId: ThirdPartyId.ExHentai,
      }),
    );
    expect(mocks.addTask).not.toHaveBeenCalled();
  });

  const chooseTaskType = async () => {
    mocks.getDefinitions.mockResolvedValue({
      data: [
        {
          thirdPartyId: ThirdPartyId.ExHentai,
          taskType: ExHentaiDownloadTaskType.SingleWork,
          name: "Single gallery",
        },
      ],
    });
    await show(undefined);
    const source = Array.from(container.querySelectorAll("button")).find(
      (button) => button.textContent === "enum.thirdPartyId.exHentai",
    )!;

    await act(async () => source.click());
    const type = Array.from(container.querySelectorAll("button")).find(
      (button) => button.textContent === "Single gallery",
    )!;

    await act(async () => type.click());
  };

  it("prefills the effective inherited directory when the source has no configured directory", async () => {
    mocks.getOptions.mockResolvedValue({
      data: { defaultPath: null, effectiveDefaultPath: "/nas-bakabase/Downloads/ExHentai" },
    });
    await chooseTaskType();
    expect(container.querySelector('[aria-label="download-directory"]')).toHaveValue(
      "/nas-bakabase/Downloads/ExHentai",
    );
  });

  it("uses the ExHentai default BT combination for new tasks", async () => {
    mocks.defaults = {
      downloadMode: ExHentaiDownloadMode.Torrent,
      saveTorrentFile: true,
      fallbackToDirect: false,
    };
    await chooseTaskType();
    expect(mocks.modeOptions).toHaveBeenLastCalledWith(mocks.defaults);
    expect(mocks.modeContext).toHaveBeenLastCalledWith(false);
  });

  it("keeps an existing task's PascalCase BT preference ahead of current defaults", async () => {
    mocks.defaults = { downloadMode: ExHentaiDownloadMode.Direct };
    mocks.tasks[0].options = '{"DownloadMode":2,"SaveTorrentFile":true,"FallbackToDirect":false}';
    await show(17);
    expect(mocks.modeOptions).toHaveBeenLastCalledWith({
      downloadMode: ExHentaiDownloadMode.Torrent,
      saveTorrentFile: true,
      fallbackToDirect: false,
    });
    expect(mocks.modeContext).toHaveBeenLastCalledWith(true);
    expect(mocks.putTask).not.toHaveBeenCalled();
  });

  it("keeps a task's manual directory when options finish loading", async () => {
    let resolveOptions!: (response: unknown) => void;

    mocks.getOptions.mockReturnValue(
      new Promise((resolve) => {
        resolveOptions = resolve;
      }),
    );
    await chooseTaskType();
    await act(async () =>
      fireEvent.change(container.querySelector('[aria-label="download-directory"]')!, {
        target: { value: "/task-override" },
      }),
    );
    await act(async () =>
      resolveOptions({
        data: { defaultPath: null, effectiveDefaultPath: "/nas-bakabase/Downloads/ExHentai" },
      }),
    );
    expect(container.querySelector('[aria-label="download-directory"]')).toHaveValue(
      "/task-override",
    );
  });

  it("preserves an existing task's saved directory", async () => {
    mocks.getOptions.mockResolvedValue({ data: { effectiveDefaultPath: "/new-default/ExHentai" } });
    await show(17);
    expect(container.querySelector('[aria-label="download-directory"]')).toHaveValue("/downloads");
  });
});
