import { HeroUIProvider, Listbox, ListboxItem } from "@heroui/react";
import { act, fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

import GeneralTaskDetailModal from "../GeneralTaskDetailModal";
import GeneralTaskRow from "../GeneralTaskRow";
import type { CenterTask } from "../../downloadCenter";

const mocks = vi.hoisted(() => ({ get: vi.fn() }));

vi.mock("@/sdk/BApi", () => ({
  default: { downloadCenter: { getDownloadCenterTask: mocks.get } },
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
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
});
