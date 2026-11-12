import type { ComponentProps, ReactNode } from "react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { HeroUIProvider } from "@heroui/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import StartAcquisitionModal from "..";
import ActualModal from "@/components/bakaui/components/Modal";
const mocks = vi.hoisted(() => ({
  createDraft: vi.fn(),
  success: vi.fn(),
  started: vi.fn(),
  destroyed: vi.fn(),
}));
vi.mock("@/components/Acquire/PlanTask/api", () => ({
  planTaskApi: { createDraft: mocks.createDraft },
}));
vi.mock("@/components/bakaui", async () => ({
  Button: (await import("@/components/bakaui/components/Button")).Button,
  Spinner: () => <span role="status">loading</span>,
  Modal: (props: ComponentProps<typeof ActualModal>) => <ActualModal {...props} />,
  Input: ({
    value,
    onValueChange,
    label,
  }: {
    value: string;
    onValueChange: (value: string) => void;
    label: string;
  }) => (
    <input
      aria-label={label}
      value={value}
      onChange={(event) => onValueChange(event.target.value)}
    />
  ),
  Textarea: ({ value, onValueChange, label }: any) => (
    <textarea aria-label={label} value={value} onChange={(e) => onValueChange(e.target.value)} />
  ),
  toast: { success: mocks.success, danger: vi.fn() },
}));
const open = () =>
  render(
    <HeroUIProvider disableAnimation>
      <StartAcquisitionModal onStarted={mocks.started} onDestroyed={mocks.destroyed} />
    </HeroUIProvider>,
  );
beforeEach(() => {
  vi.clearAllMocks();
  mocks.createDraft.mockResolvedValue({ task: { id: 42 } });
});
describe("draft acquisition creation", () => {
  it("saves a name only without selecting a search builder or starting execution", async () => {
    open();
    expect(screen.getByRole("button", { name: "acquisition.draft.save" })).toBeDisabled();
    fireEvent.change(screen.getByRole("textbox", { name: "acquisition.plan.titleLabel" }), {
      target: { value: "  Resource name  " },
    });
    fireEvent.click(screen.getByRole("button", { name: "acquisition.draft.save" }));
    await waitFor(() =>
      expect(mocks.createDraft).toHaveBeenCalledExactlyOnceWith({
        title: "Resource name",
        sources: [],
      }),
    );
    expect(mocks.started).toHaveBeenCalledWith(42);
    expect(screen.queryByRole("combobox")).toBeNull();
  });
  it("allows source links without a title and preserves separate inputs", async () => {
    open();
    fireEvent.change(screen.getByRole("textbox", { name: "acquisition.draft.urls" }), {
      target: { value: "https://example.com/a\nhttps://example.com/b" },
    });
    fireEvent.click(screen.getByRole("button", { name: "acquisition.draft.save" }));
    await waitFor(() =>
      expect(mocks.createDraft).toHaveBeenCalledWith({
        title: undefined,
        sources: [
          { id: "source-1", kind: "auto", url: "https://example.com/a" },
          { id: "source-2", kind: "auto", url: "https://example.com/b" },
        ],
      }),
    );
  });
  it("retains the draft form if saving fails", async () => {
    mocks.createDraft.mockRejectedValueOnce(new Error("save failed"));
    open();
    fireEvent.change(screen.getByRole("textbox", { name: "acquisition.plan.titleLabel" }), {
      target: { value: "A name" },
    });
    fireEvent.click(screen.getByRole("button", { name: "acquisition.draft.save" }));
    await waitFor(() => expect(mocks.createDraft).toHaveBeenCalledTimes(1));
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(mocks.started).not.toHaveBeenCalled();
  });
});
