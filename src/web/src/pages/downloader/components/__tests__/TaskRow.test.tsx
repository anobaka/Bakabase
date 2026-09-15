import type { DownloadTask } from "@/core/models/DownloadTask";
import type { TaskRowProps } from "../TaskRow";

import { HeroUIProvider } from "@heroui/react";
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import TaskRow from "../TaskRow";

import { DownloadTaskAction, DownloadTaskStatus, ThirdPartyId } from "@/sdk/constants";

// Keep real HeroUI press handling and portalled menus: bubbling keyboard events were the bug.
vi.mock("@/components/bakaui", async () => ({
  ...(await import("@heroui/react")),
  Button: (await import("@/components/bakaui/components/Button")).Button,
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

async function show(overrides: Partial<DownloadTask> = {}) {
  const props: TaskRowProps = {
    task: { ...task, ...overrides },
    statusColor: "default",
    progressColor: "primary",
    formatDateTime: (value) => String(value),
    onStart: vi.fn(),
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
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

describe("download task row interaction", () => {
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
    ["common.action.openFolder", "onOpenFolder", task.downloadPath],
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
    const props = await show({ downloadPath: undefined });
    const button = element("common.action.openFolder");

    expect(button).toBeDisabled();
    await click(button);
    expect(props.onOpenFolder).not.toHaveBeenCalled();
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
    expect(container).toHaveTextContent("downloader.label.torrentAvailable");
    expect(container).not.toHaveTextContent("downloader.results.contentsReady");
    const schedule = document.querySelector('[aria-label^="downloader.label.createdAt"]')!;

    expect(schedule).toHaveAttribute("title", expect.stringContaining(task.createdAt));
    expect(schedule).toHaveAttribute("title", expect.stringContaining(String(nextStartDt)));
  });
});
