import { HeroUIProvider, Listbox, ListboxItem } from "@heroui/react";
import { act, fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

import GeneralTaskDetailModal from "../GeneralTaskDetailModal";
import GeneralTaskRow from "../GeneralTaskRow";
import type { CenterTask } from "../../downloadCenter";

const mocks = vi.hoisted(() => ({ get: vi.fn(), portal: vi.fn() }));

vi.mock("@/sdk/BApi", () => ({
  default: { downloadCenter: { getDownloadCenterTask: mocks.get } },
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: mocks.portal }),
}));
vi.mock("@/components/Workflow/WorkflowRunDetailModal", () => ({ default: () => null }));

const task: CenterTask = {
  id: "bt:example",
  providerTaskId: "example",
  category: "general",
  provider: "bt",
  name: "Downloaded gallery",
  outputDirectory: "/downloads/gallery",
  status: "running",
  availableActions: ["stop", "delete"],
  createdAt: "2026-10-19",
  updatedAt: "2026-10-19",
  originKind: "manual",
  files: ["gallery/image.jpg"],
};

function mount() {
  const onSelect = vi.fn();
  const onAction = vi.fn();
  const onSource = vi.fn();
  const onFolder = vi.fn();
  mocks.get.mockResolvedValue({ code: 0, data: task });
  function Harness() {
    const [selectedTask, setSelectedTask] = useState<CenterTask>();
    return (
      <HeroUIProvider disableAnimation>
        <Listbox aria-label="Downloads">
          <ListboxItem key={task.id} textValue={task.name || task.id}>
            <GeneralTaskRow
              task={task}
              onSelect={onSelect}
              onAction={onAction}
              onSource={onSource}
              onFolder={onFolder}
              onDetail={(id) => id === task.id && setSelectedTask(task)}
            />
          </ListboxItem>
        </Listbox>
        {selectedTask && (
          <GeneralTaskDetailModal
            task={selectedTask}
            onAction={onAction}
            onSource={onSource}
            onFolder={onFolder}
            onDestroyed={() => setSelectedTask(undefined)}
          />
        )}
      </HeroUIProvider>
    );
  }
  render(<Harness />);
  return { onSelect, onAction, onSource, onFolder };
}

afterEach(() => vi.clearAllMocks());

describe("general download task details", () => {
  it("can review an eligible site task before its detailed site row has loaded", async () => {
    const available = { ...task, category: "site", provider: "baidu", id: "site:18", siteTaskId: 18, availableActions: ["verifyExistingFiles"] };
    const props = { onSelect: vi.fn(), onAction: vi.fn(), onSource: vi.fn(), onFolder: vi.fn() };
    const row = (entry: CenterTask) => <HeroUIProvider disableAnimation><GeneralTaskRow task={entry} {...props} /></HeroUIProvider>;
    const view = render(row(available));
    const label = "downloadCenter.existingFiles.action";
    fireEvent.click(screen.getByRole("button", { name: label }));
    expect(mocks.portal).toHaveBeenCalledWith(expect.any(Function), { task: available });
    expect(props.onAction).not.toHaveBeenCalled();
    view.rerender(row({ ...available, stage: "VerifyingSource" }));
    expect(screen.queryByRole("button", { name: label })).toBeNull();
    view.rerender(row({ ...available, availableActions: ["retry"] }));
    expect(screen.queryByRole("button", { name: label })).toBeNull();
  });
  it("opens the actual detail dialog from a list row without selecting or changing the task", async () => {
    const callbacks = mount();
    fireEvent.click(screen.getByRole("button", { name: "downloadCenter.action.detail" }));
    expect(await screen.findByRole("dialog")).toHaveTextContent(task.outputDirectory);
    expect(mocks.get).toHaveBeenCalledWith(task.id, { showErrorToast: false });
    for (const callback of Object.values(callbacks)) expect(callback).not.toHaveBeenCalled();
  });

  it.each(["button", "icon"])(
    "opens task details after pointer down/up on its %s",
    async (target) => {
      const { onSelect, onAction } = mount();
      const button = screen.getByRole("button", { name: "downloadCenter.action.detail" });
      await userEvent.click(target === "icon" ? button.querySelector("svg")! : button);
      expect(await screen.findByRole("dialog")).toHaveTextContent(task.outputDirectory);
      expect(onSelect).not.toHaveBeenCalled();
      expect(onAction).not.toHaveBeenCalled();
    },
  );

  it.each(["Enter", " "])("opens task details using %s without selecting the row", async (key) => {
    const { onSelect } = mount();
    const button = screen.getByRole("button", { name: "downloadCenter.action.detail" });
    await act(async () => button.focus());
    fireEvent.keyDown(button, { key, code: key === " " ? "Space" : key });
    fireEvent.keyUp(button, { key, code: key === " " ? "Space" : key });
    expect(await screen.findByRole("dialog")).toBeInTheDocument();
    expect(onSelect).not.toHaveBeenCalled();
  });

  it("retains a pressed detail button when a virtualized row receives a new progress snapshot", async () => {
    const rect = vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockReturnValue(
      new DOMRect(0, 0, 800, 500),
    );
    const onSelect = vi.fn();
    const onDetail = vi.fn();
    const onAction = vi.fn();
    const onSource = vi.fn();
    const onFolder = vi.fn();
    const list = (currentTask: CenterTask) => (
      <HeroUIProvider disableAnimation>
        <Listbox
          aria-label="Downloads"
          isVirtualized
          virtualization={{ maxListboxHeight: 500, itemHeight: 128 }}
        >
          <ListboxItem key={currentTask.id} textValue={currentTask.name || currentTask.id}>
            <GeneralTaskRow
              task={currentTask}
              onSelect={onSelect}
              onDetail={onDetail}
              onAction={onAction}
              onSource={onSource}
              onFolder={onFolder}
            />
          </ListboxItem>
        </Listbox>
      </HeroUIProvider>
    );
    try {
      const view = render(list(task));
      const button = await screen.findByRole("button", { name: "downloadCenter.action.detail" });
      const user = userEvent.setup();
      await user.pointer({ target: button, keys: "[MouseLeft>]" });
      view.rerender(list({ ...task, progress: 50, completedBytes: 1024 }));
      expect(screen.getByRole("button", { name: "downloadCenter.action.detail" })).toBe(button);
      await user.pointer({ target: button, keys: "[/MouseLeft]" });
      expect(onDetail).toHaveBeenCalledExactlyOnceWith(task.id);
      expect(onSelect).not.toHaveBeenCalled();
      expect(onAction).not.toHaveBeenCalled();
    } finally {
      rect.mockRestore();
    }
  });
});
