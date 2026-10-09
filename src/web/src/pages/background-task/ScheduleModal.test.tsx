import type { ReactNode } from "react";
import type { BTask } from "@/core/models/BTask";

import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import ScheduleModal from "./ScheduleModal";

const api = vi.hoisted(() => ({ get: vi.fn(), patch: vi.fn(), success: vi.fn() }));

vi.mock("@/sdk/BApi", () => ({
  default: { options: { getTaskOptions: api.get, patchTaskOptions: api.patch } },
}));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ children, footer }: { children: ReactNode; footer: ReactNode }) => (
    <div role="dialog">
      {children}
      {footer}
    </div>
  ),
  Button: ({
    children,
    onPress,
    isLoading,
    isDisabled,
  }: {
    children: ReactNode;
    onPress: () => void;
    isLoading?: boolean;
    isDisabled?: boolean;
  }) => (
    <button disabled={isLoading || isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Input: ({
    label,
    value,
    onValueChange,
    isDisabled,
  }: {
    label: string;
    value: string;
    onValueChange: (value: string) => void;
    isDisabled?: boolean;
  }) => (
    <label>
      {label}
      <input
        disabled={isDisabled}
        value={value}
        onChange={(event) => onValueChange(event.target.value)}
      />
    </label>
  ),
  Select: ({
    label,
    selectedKeys,
    onSelectionChange,
    dataSource,
    isDisabled,
  }: {
    label: string;
    selectedKeys: string[];
    onSelectionChange: (keys: Set<string>) => void;
    dataSource: { value: string; label: string }[];
    isDisabled?: boolean;
  }) => (
    <label>
      {label}
      <select
        disabled={isDisabled}
        value={selectedKeys[0]}
        onChange={(event) => onSelectionChange(new Set([event.target.value]))}
      >
        {dataSource.map(({ value, label }) => (
          <option key={value} value={value}>
            {label}
          </option>
        ))}
      </select>
    </label>
  ),
  toast: { success: api.success },
}));

const task = { id: "test-task", name: "Test", interval: "00:05:00", isPersistent: true } as BTask;

beforeEach(() => {
  vi.resetAllMocks();
  api.get.mockResolvedValue({
    code: 0,
    data: {
      tasks: [
        { id: "not-registered", interval: "01:00:00" },
        { id: task.id, interval: "00:05:00" },
      ],
    },
  });
  api.patch.mockResolvedValue({ code: 0 });
});

describe("schedule editor", () => {
  it("saves only on explicit confirmation and retains other saved tasks", async () => {
    const close = vi.fn();

    render(<ScheduleModal task={task} onClose={close} />);
    fireEvent.change(screen.getByLabelText("backgroundTask.column.interval"), {
      target: { value: "15" },
    });
    fireEvent.blur(screen.getByLabelText("backgroundTask.column.interval"));
    expect(api.patch).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("common.action.save"));
    await waitFor(() => expect(close).toHaveBeenCalledOnce());
    expect(api.patch).toHaveBeenCalledWith(
      {
        tasks: [
          { id: "not-registered", interval: "01:00:00" },
          { id: task.id, interval: "00:15:00", enableAfter: undefined },
        ],
      },
      { showErrorToast: false },
    );
    expect(api.success).toHaveBeenCalledOnce();
  });
  it("cancel does not write options", () => {
    const close = vi.fn();

    render(<ScheduleModal task={task} onClose={close} />);
    fireEvent.change(screen.getByLabelText("backgroundTask.column.interval"), {
      target: { value: "15" },
    });
    fireEvent.click(screen.getByText("common.action.cancel"));
    expect(close).toHaveBeenCalledOnce();
    expect(api.get).not.toHaveBeenCalled();
    expect(api.patch).not.toHaveBeenCalled();
  });
  it("keeps edits after an error envelope and allows retry", async () => {
    const close = vi.fn();

    api.patch.mockResolvedValueOnce({ code: 400, message: "Schedule refused" });
    render(<ScheduleModal task={task} onClose={close} />);
    fireEvent.change(screen.getByLabelText("backgroundTask.column.interval"), {
      target: { value: "12" },
    });
    fireEvent.click(screen.getByText("common.action.save"));
    expect(await screen.findByRole("alert")).toHaveTextContent("Schedule refused");
    expect(screen.getByLabelText("backgroundTask.column.interval")).toHaveValue("12");
    expect(close).not.toHaveBeenCalled();
    expect(api.success).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("common.action.save"));
    await waitFor(() => expect(close).toHaveBeenCalledOnce());
    expect(api.patch).toHaveBeenCalledTimes(2);
  });
  it("never replaces options when the current configuration cannot be read", async () => {
    api.get.mockResolvedValueOnce({ code: 500, message: "Read failed" });
    render(<ScheduleModal task={task} onClose={vi.fn()} />);
    fireEvent.click(screen.getByText("common.action.save"));
    expect(await screen.findByRole("alert")).toHaveTextContent("Read failed");
    expect(api.patch).not.toHaveBeenCalled();
  });
  it("keeps the server's wall-clock date and validates an empty existing gate", async () => {
    render(
      <ScheduleModal task={{ ...task, enableAfter: "2026-10-10 12:30:00" }} onClose={vi.fn()} />,
    );
    const date = screen.getByLabelText("backgroundTask.schedule.enableAfter");

    expect(date).toHaveValue("2026-10-10T12:30:00");
    fireEvent.change(date, { target: { value: "" } });
    fireEvent.click(screen.getByText("common.action.save"));
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "backgroundTask.schedule.invalidEnableAfter",
    );
    expect(api.patch).not.toHaveBeenCalled();
  });
  it("disables repeated submits until the save finishes", async () => {
    let finish!: (value: unknown) => void;

    api.patch.mockReturnValue(
      new Promise((resolve) => {
        finish = resolve;
      }),
    );
    render(<ScheduleModal task={task} onClose={vi.fn()} />);
    const button = screen.getByText("common.action.save");

    fireEvent.click(button);
    fireEvent.click(button);
    await waitFor(() => expect(api.patch).toHaveBeenCalledOnce());
    expect(button).toBeDisabled();
    finish({ code: 0 });
    await waitFor(() => expect(button).not.toBeDisabled());
  });
});
