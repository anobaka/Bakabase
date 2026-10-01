import type { DownloadTask } from "@/core/models/DownloadTask";
import type { TaskRowProps } from "../TaskRow";

import { HeroUIProvider } from "@heroui/react";
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import TaskRow from "../TaskRow";

import { DownloadTaskAction, DownloadTaskStatus, ThirdPartyId } from "@/sdk/constants";

const { dangerToast } = vi.hoisted(() => ({ dangerToast: vi.fn() }));

// Keep real HeroUI press handling and portalled menus: bubbling keyboard events were the bug.
vi.mock("@/components/bakaui", async () => ({
  ...(await import("@heroui/react")),
  Button: (await import("@/components/bakaui/components/Button")).Button,
  toast: { danger: dangerToast },
}));
vi.mock("@/components/ThirdPartyIcon", () => ({ default: () => <span>ExHentai</span> }));

const task: DownloadTask = {
  id: 17,
  name: "Gallery with several files",
  key: "https://exhentai.org/g/123/example/",
  thirdPartyId: ThirdPartyId.ExHentai,
  type: 1,
  progress: 42.3,
  downloadStatusUpdateDt: new Date("2026-09-14T01:00:00Z"),
  status: DownloadTaskStatus.Idle,
  downloadPath: "/downloads/gallery",
  failureTimes: 0,
  autoRetry: false,
  availableActions: [DownloadTaskAction.StartManually],
  displayName: "Gallery with several files",
  canStart: true,
  createdAt: "2026-09-14T01:00:00Z",
};

let container: HTMLDivElement;
let root: Root;
let originalClipboard: PropertyDescriptor | undefined;

function element(label: string) {
  return document.querySelector<HTMLElement>(`[aria-label="${label}"]`)!;
}

async function click(target: HTMLElement, init: MouseEventInit = {}) {
  await act(async () => {
    target.dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true, ...init }));
  });
}

async function key(target: HTMLElement, value: string) {
  await act(async () => {
    target.focus();
    for (const type of ["keydown", "keyup"]) {
      target.dispatchEvent(
        new KeyboardEvent(type, {
          key: value,
          code: value === " " ? "Space" : value,
          bubbles: true,
          cancelable: true,
        }),
      );
    }
  });
}

async function show(
  overrides: Partial<DownloadTask> = {},
  onDownloadDirectly = vi.fn().mockResolvedValue(undefined),
) {
  const props: TaskRowProps = {
    task: { ...task, ...overrides },
    statusColor: "default",
    progressColor: "primary",
    formatDateTime: (value) => String(value),
    onStart: vi.fn(),
    onDownloadDirectly,
    onStop: vi.fn(),
    onEdit: vi.fn(),
    onOpenFolder: vi.fn(),
    onDelete: vi.fn(),
    onShowError: vi.fn(),
    onClick: vi.fn(),
    onContextMenu: vi.fn(),
  };

  await act(async () =>
    root.render(
      <HeroUIProvider disableAnimation>
        <TaskRow {...props} />
      </HeroUIProvider>,
    ),
  );

  return props;
}

beforeEach(() => {
  dangerToast.mockClear();
  originalClipboard = Object.getOwnPropertyDescriptor(navigator, "clipboard");
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
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
});

describe("download task row interaction", () => {
  it("shows the attributed file size before the progress percent", async () => {
    await show({ downloadedBytes: 1572864 });

    expect(element("downloader.label.downloadedFileSize: 1.5 MiB")).toHaveTextContent("1.5 MiB");
    expect(container).toHaveTextContent("42%");
  });

  it("omits file size until a file is attributed", async () => {
    await show();

    expect(
      document.querySelector('[aria-label^="downloader.label.downloadedFileSize:"]'),
    ).toBeNull();
  });

  it.each([null, undefined])(
    "does not invent a completed date when it is %s",
    async (completedAt) => {
      await show({ status: DownloadTaskStatus.Complete, completedAt });

      expect(container).not.toHaveTextContent("downloader.label.completedAt");
      expect(container).toHaveTextContent(`downloader.label.createdAt ${task.createdAt}`);
    },
  );

  it("shows the last successful completion even after a later failure", async () => {
    const completedAt = "2026-09-15T02:03:04Z";

    await show({ status: DownloadTaskStatus.Failed, completedAt });

    const dates = document.querySelector<HTMLElement>(
      '[aria-label^="downloader.label.createdAt"]',
    )!;

    expect(dates).toHaveTextContent(`downloader.label.completedAt ${completedAt}`);
    expect(dates).toHaveTextContent(`downloader.label.createdAt ${task.createdAt}`);
  });

  it("keeps the next start and original creation date available beside the completion", async () => {
    const completedAt = "2026-09-15T02:03:04Z";
    const nextStartDt = new Date("2026-09-16T05:00:00Z");

    await show({ completedAt, nextStartDt });

    const dates = document.querySelector<HTMLElement>(
      '[aria-label^="downloader.label.createdAt"]',
    )!;

    expect(dates).toHaveTextContent(`downloader.label.completedAt ${completedAt}`);
    expect(dates).toHaveTextContent(`downloader.label.nextStartTime ${String(nextStartDt)}`);
    expect(dates.title).toContain(`downloader.label.createdAt ${task.createdAt}`);
  });

  it("copies the download key without selecting the row and keeps clicking its text unchanged", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);

    Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText } });
    const props = await show();
    const address = document.querySelector<HTMLElement>(`[title="${task.key}"]`)!;

    expect(address.closest("a")).toBeNull();
    await click(element("downloader.action.copyDownloadLink"));
    expect(writeText).toHaveBeenCalledExactlyOnceWith(task.key);
    expect(props.onClick).not.toHaveBeenCalled();
    await click(address);
    expect(props.onClick).toHaveBeenCalledExactlyOnceWith(task.id, expect.anything());
  });

  it("orders size, speed, remaining time, and percent while downloading", async () => {
    const estimate =
      "downloader.label.estimatedRemaining 1datetime.duration.minute 5datetime.duration.second";

    await show({
      status: DownloadTaskStatus.Downloading,
      downloadedBytes: 1572864,
      downloadSpeedBytesPerSecond: 1536,
      estimatedRemainingSeconds: 65,
    });

    const size = element("downloader.label.downloadedFileSize: 1.5 MiB");
    const speed = element("downloader.label.downloadSpeed: 1.5 KiB/s");
    const remaining = document.querySelector<HTMLElement>(`[title="${estimate}"]`)!;
    const percent = remaining.parentElement!.lastElementChild!;

    expect([size, speed, remaining, percent].map((node) => node.textContent)).toEqual([
      "1.5 MiB",
      "1.5 KiB/s",
      estimate,
      "42%",
    ]);
    expect(size.compareDocumentPosition(speed) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(
      speed.compareDocumentPosition(remaining) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
    expect(
      remaining.compareDocumentPosition(percent) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
    expect(
      Array.from(size.parentElement!.children).filter((node) => node.textContent === "·"),
    ).toHaveLength(3);
  });

  it.each([
    [DownloadTaskStatus.Idle, 1536],
    [DownloadTaskStatus.Complete, 1536],
    [DownloadTaskStatus.Downloading, null],
    [DownloadTaskStatus.Downloading, 0],
    [DownloadTaskStatus.Downloading, -1],
    [DownloadTaskStatus.Downloading, Number.NaN],
  ])("omits unavailable speed for status %s and value %s", async (status, speed) => {
    await show({ status, downloadSpeedBytesPerSecond: speed });

    expect(document.querySelector('[aria-label^="downloader.label.downloadSpeed:"]')).toBeNull();
    expect(container).not.toHaveTextContent("·");
  });

  it("shows the server estimate beside the progress percentage, not among the dates", async () => {
    const estimate =
      "downloader.label.estimatedRemaining 1datetime.duration.minute 5datetime.duration.second";

    await show({ status: DownloadTaskStatus.Downloading, estimatedRemainingSeconds: 65 });

    const readouts = document.querySelectorAll<HTMLElement>(`[title="${estimate}"]`);

    expect(readouts).toHaveLength(1);
    expect(readouts[0]).toHaveTextContent(estimate);
    // Same line as the percentage, which stays last so it keeps its column in every row.
    expect(readouts[0].parentElement!.lastElementChild).toHaveTextContent("42%");
    // And nowhere in the dates line it used to open.
    expect(
      document.querySelector('[aria-label^="downloader.label.createdAt"]')!.parentElement,
    ).not.toHaveTextContent("downloader.label.estimatedRemaining");
    expect(document.querySelector('[role="progressbar"]')).toHaveAttribute("aria-valuenow", "42.3");
  });

  it("does not leave a separator behind when there is no estimate", async () => {
    await show({ status: DownloadTaskStatus.Downloading, estimatedRemainingSeconds: undefined });

    expect(container).not.toHaveTextContent("downloader.label.estimatedRemaining");
    expect(container).not.toHaveTextContent("·");
  });

  it("keeps row click modifiers and the context menu callbacks", async () => {
    const props = await show();
    const title = document.querySelector<HTMLElement>(`[title="${task.name}"]`)!;

    await click(title, { ctrlKey: true, shiftKey: true });
    expect(props.onClick).toHaveBeenCalledWith(
      task.id,
      expect.objectContaining({ ctrlKey: true, shiftKey: true }),
    );
    await act(async () => {
      title.dispatchEvent(new MouseEvent("contextmenu", { bubbles: true }));
    });
    expect(props.onContextMenu).toHaveBeenCalledWith(task.id, expect.anything());
  });

  it("selects a focused row once with Enter or Space", async () => {
    const props = await show();
    const row = element(task.name!);

    await key(row, "Enter");
    await key(row, " ");
    expect(props.onClick).toHaveBeenCalledTimes(2);
    expect(props.onStart).not.toHaveBeenCalled();
  });

  it.each([
    [DownloadTaskAction.StartManually, "downloader.action.start", "onStart"],
    [DownloadTaskAction.Restart, "downloader.action.restart", "onStart"],
    [DownloadTaskAction.Disable, "downloader.action.stop", "onStop"],
  ] as const)(
    "isolates action %s for pointer, Enter and Space",
    async (action, label, callback) => {
      const props = await show({ availableActions: [action] });
      const button = element(label);

      await click(button);
      await key(button, "Enter");
      await key(button, " ");
      expect(props[callback]).toHaveBeenCalledTimes(3);
      expect(props[callback]).toHaveBeenLastCalledWith(task.id);
      expect(props.onClick).not.toHaveBeenCalled();
    },
  );

  it.each([
    ["downloader.action.edit", "onEdit", task.id],
    ["common.action.openFolder", "onOpenFolder", task],
  ] as const)(
    "isolates %s from row selection and context menus",
    async (label, callback, argument) => {
      const props = await show();
      const button = element(label);

      await click(button);
      await key(button, "Enter");
      await key(button, " ");
      await act(async () => {
        button.dispatchEvent(new MouseEvent("contextmenu", { bubbles: true }));
      });
      expect(props[callback]).toHaveBeenCalledTimes(3);
      expect(props[callback]).toHaveBeenLastCalledWith(argument);
      expect(props.onClick).not.toHaveBeenCalled();
      expect(props.onContextMenu).not.toHaveBeenCalled();
    },
  );

  it("keeps the error visible and opens its detail without selecting the row", async () => {
    const props = await show({
      status: DownloadTaskStatus.Failed,
      failureTimes: 2,
      message: "The download connection was interrupted.",
      availableActions: [DownloadTaskAction.Restart],
    });
    const button = element("downloader.action.showError");

    expect(button).toHaveTextContent(props.task.message!);
    await click(button);
    await key(button, "Enter");
    await key(button, " ");
    await act(async () => {
      button.dispatchEvent(new MouseEvent("contextmenu", { bubbles: true }));
    });
    expect(props.onContextMenu).not.toHaveBeenCalled();
    expect(props.onShowError).toHaveBeenCalledTimes(3);
    expect(props.onShowError).toHaveBeenLastCalledWith(props.task);
    expect(props.onClick).not.toHaveBeenCalled();
  });

  it.each([0, undefined])("does not invent a failure count when it is %s", async (failureTimes) => {
    await show({
      status: DownloadTaskStatus.Failed,
      failureTimes,
      message: "The task was interrupted after a restart.",
    });
    const button = element("downloader.action.viewError");

    expect(button).toBeInTheDocument();
    expect(button).toHaveTextContent("The task was interrupted after a restart.");
    expect(button).toHaveAttribute(
      "title",
      "downloader.action.viewError: The task was interrupted after a restart.",
    );
    expect(element("downloader.action.showError")).not.toBeInTheDocument();
  });

  it("offers a completed task's notes by their summary line and opens them without selecting the row", async () => {
    const message = [
      "2 note(s) from this task:",
      "- av1 Some video — skipped: deleted or no longer visible",
      "- av2 Another video — skipped: interactive videos are not supported",
      "",
      "Skipped items are not retried automatically.",
    ].join("\n");
    const props = await show({
      status: DownloadTaskStatus.Complete,
      progress: 100,
      current: "Done",
      message,
    });
    const button = element("downloader.action.viewNotices");

    expect(button).toBeInTheDocument();
    expect(button).toHaveTextContent("2 note(s) from this task:");
    expect(button).not.toHaveTextContent("- av1");
    expect(button).toHaveAttribute(
      "title",
      "downloader.action.viewNotices: 2 note(s) from this task:",
    );
    // Informational, not an error.
    expect(element("downloader.action.viewError")).not.toBeInTheDocument();
    expect(container).not.toHaveTextContent("Done");

    await click(button);
    await act(async () => {
      button.dispatchEvent(new MouseEvent("contextmenu", { bubbles: true }));
    });
    expect(props.onShowError).toHaveBeenCalledExactlyOnceWith(props.task);
    expect(props.onClick).not.toHaveBeenCalled();
    expect(props.onContextMenu).not.toHaveBeenCalled();
  });

  it("opens a completed task's notes from the keyboard", async () => {
    const props = await show({
      status: DownloadTaskStatus.Complete,
      message: "1 item was skipped:",
    });
    const button = element("downloader.action.viewNotices");

    await key(button, "Enter");
    await key(button, " ");
    expect(props.onShowError).toHaveBeenCalledTimes(2);
    expect(props.onClick).not.toHaveBeenCalled();
  });

  it.each([undefined, ""])(
    "keeps the progress text on a completed task without notes (%o)",
    async (message) => {
      await show({ status: DownloadTaskStatus.Complete, current: "All files downloaded", message });

      expect(element("downloader.action.viewNotices")).not.toBeInTheDocument();
      expect(container).toHaveTextContent("All files downloaded");
    },
  );

  it.each([DownloadTaskStatus.Idle, DownloadTaskStatus.Downloading, DownloadTaskStatus.Disabled])(
    "does not offer notes for a task in status %s",
    async (status) => {
      await show({ status, message: "Left over from an earlier run" });

      expect(element("downloader.action.viewNotices")).not.toBeInTheDocument();
    },
  );

  it("shows a failed task's error, not notes, even when it has several lines", async () => {
    await show({
      status: DownloadTaskStatus.Failed,
      message: "Bilibili is temporarily refusing requests.\n\n1 item(s) were skipped:",
    });

    expect(element("downloader.action.viewError")).toBeInTheDocument();
    expect(element("downloader.action.viewNotices")).not.toBeInTheDocument();
  });

  it.each(["pointer", "keyboard"])(
    "deletes from the portalled menu using %s without selecting the row",
    async (method) => {
      const props = await show();
      const more = element("common.action.more");

      if (method === "pointer") {
        await click(more);
        await click(document.querySelector<HTMLElement>('[role="menuitem"]')!);
      } else {
        await key(more, "Enter");
        await key(document.querySelector<HTMLElement>('[role="menuitem"]')!, "Enter");
      }
      expect(props.onDelete).toHaveBeenCalledExactlyOnceWith(task.id);
      expect(props.onClick).not.toHaveBeenCalled();
    },
  );

  it("does not open an undefined download folder", async () => {
    const props = await show({ thirdPartyId: ThirdPartyId.Bilibili, downloadPath: undefined });
    const button = element("common.action.openFolder");

    expect(button).toBeDisabled();
    await click(button);
    expect(props.onOpenFolder).not.toHaveBeenCalled();
    expect(props.onClick).not.toHaveBeenCalled();
  });

  it("lets ExHentai locate persisted output without a configured folder", async () => {
    const props = await show({ downloadPath: undefined });
    const button = element("common.action.openFolder");

    expect(button).not.toBeDisabled();
    await click(button);
    expect(props.onOpenFolder).toHaveBeenCalledExactlyOnceWith(props.task);
    expect(props.onClick).not.toHaveBeenCalled();
  });

  it("keeps torrent availability separate from completed content and retains both dates", async () => {
    const nextStartDt = new Date("2026-09-15T01:00:00Z");

    await show({
      status: DownloadTaskStatus.Complete,
      progress: 100,
      nextStartDt,
      metadata: { torrentFoundAt: "2026-09-14T01:01:00Z" },
    });
    expect(document.querySelector('[role="progressbar"]')).toHaveAttribute("aria-valuenow", "100");
    expect(element("downloader.label.torrentAvailable")).toHaveAttribute("aria-haspopup", "true");
    expect(container).not.toHaveTextContent("downloader.results.contentsReady");
    const schedule = document.querySelector('[aria-label^="downloader.label.createdAt"]')!;

    expect(schedule).toHaveAttribute("title", expect.stringContaining(task.createdAt));
    expect(schedule).toHaveAttribute("title", expect.stringContaining(String(nextStartDt)));
  });

  it.each([
    [
      { torrentFoundAt: "2026-09-14T01:01:00Z" },
      "downloader.label.torrentAvailable",
      "text-success",
    ],
    [
      { noTorrentCheckedAt: "2026-09-14T01:02:00Z" },
      "downloader.label.torrentUnavailable",
      "text-warning",
    ],
    [{ preferTorrent: false }, "downloader.label.torrentDisabled", "text-default-400"],
  ] as const)(
    "shows the torrent state %o as an icon right after the task name",
    async (metadata, label, color) => {
      await show({ metadata });
      const indicator = element(label);

      expect(indicator.tagName).toBe("BUTTON");
      expect(indicator).toHaveAttribute("aria-haspopup", "true");
      expect(indicator).toHaveClass(color);
      expect(indicator.querySelector("svg")).toBeInTheDocument();
      // An icon, not a chip: the label is for assistive technology, not rendered text.
      expect(container).not.toHaveTextContent(label);
      expect(indicator.closest("[data-task-action]")!.previousElementSibling).toHaveAttribute(
        "title",
        task.name,
      );
    },
  );

  it("keeps the no-torrent explanation in the icon's tooltip", async () => {
    await show({ metadata: { noTorrentCheckedAt: "2026-09-14T01:02:00Z" } });
    const indicator = element("downloader.label.torrentUnavailable");

    // jsdom cannot emulate react-aria hover; focus opens the same tooltip, and also proves the icon
    // is reachable without a mouse.
    await act(async () => indicator.focus());

    expect(document.querySelector('[role="tooltip"]')).toHaveTextContent(
      "downloader.tip.noTorrentCheckedAt",
    );
  });

  it.each([undefined, { preferTorrent: true }])(
    "offers direct download before an ExHentai task has been probed (%o)",
    async (metadata) => {
      await show({ metadata });

      expect(element("downloader.label.torrentUnknown")).toHaveAttribute("aria-haspopup", "true");
      await click(element("downloader.label.torrentUnknown"));
      expect(document.querySelector('[role="menuitem"]')).toHaveTextContent(
        "downloader.action.directDownload",
      );
    },
  );

  it("does not offer ExHentai download actions for another source", async () => {
    await show({ thirdPartyId: ThirdPartyId.Steam, metadata: { preferTorrent: false } });

    expect(element("downloader.label.torrentDisabled")).not.toBeInTheDocument();
    expect(element("downloader.label.torrentUnknown")).not.toBeInTheDocument();
  });

  it.each(["pointer", "keyboard"])(
    "downloads directly from the torrent menu using %s without selecting or normally starting the row",
    async (method) => {
      const props = await show({ metadata: { torrentFoundAt: "2026-09-14T01:01:00Z" } });
      const trigger = element("downloader.label.torrentAvailable");

      if (method === "pointer") {
        await click(trigger);
        await click(document.querySelector<HTMLElement>('[role="menuitem"]')!);
      } else {
        await key(trigger, "Enter");
        await key(document.querySelector<HTMLElement>('[role="menuitem"]')!, "Enter");
      }

      expect(props.onDownloadDirectly).toHaveBeenCalledExactlyOnceWith(task.id);
      expect(props.onStart).not.toHaveBeenCalled();
      expect(props.onClick).not.toHaveBeenCalled();
      expect(props.onContextMenu).not.toHaveBeenCalled();
    },
  );

  it("blocks repeated direct downloads while the request or conflict choice is pending", async () => {
    let finish!: () => void;
    const pending = new Promise<void>((resolve) => {
      finish = resolve;
    });
    const download = vi.fn(() => pending);

    await show({}, download);
    const trigger = element("downloader.label.torrentUnknown");

    await click(trigger);
    await click(document.querySelector<HTMLElement>('[role="menuitem"]')!);
    expect(trigger).toBeDisabled();
    await click(trigger);
    expect(download).toHaveBeenCalledTimes(1);

    await act(async () => finish());
    expect(trigger).not.toBeDisabled();
  });

  it.each([new Error("Cookie expired"), { code: 400, message: "Cookie expired" }])(
    "surfaces a failed direct download and permits retry (%o)",
    async (error) => {
      const download = vi.fn().mockRejectedValue(error);

      await show({}, download);
      const trigger = element("downloader.label.torrentUnknown");

      await click(trigger);
      await click(document.querySelector<HTMLElement>('[role="menuitem"]')!);

      expect(dangerToast).toHaveBeenCalledExactlyOnceWith("Cookie expired");
      expect(trigger).not.toBeDisabled();
    },
  );
});
