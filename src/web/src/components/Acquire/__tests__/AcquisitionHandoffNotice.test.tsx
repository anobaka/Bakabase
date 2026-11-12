import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { HeroUIProvider } from "@heroui/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import AcquisitionHandoffNotice from "../AcquisitionHandoffNotice";

vi.mock("@/components/bakaui", async () => await import("@heroui/react"));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
const handoff = {
  id: "postParser:task:1:2",
  source: { module: "postParser", entityKind: "task", entityId: "1", revision: "2" },
  status: "queued",
  updatedAt: "2026-10-23T00:00:00Z",
};
afterEach(cleanup);

describe("acquisition handoff status", () => {
  it("does not show an obsolete handoff warning after the related task has started", () => {
    render(
      <HeroUIProvider>
        <AcquisitionHandoffNotice
          handoff={{
            ...handoff,
            status: "attention",
            code: "HANDOFF_PLAN_CHANGED",
            acquisitionTaskId: 12,
          }}
          hasTaskSummary
          hasStartedTask
          onChanged={vi.fn()}
        />
      </HeroUIProvider>,
    );
    expect(screen.queryByText("acquire.handoff.status.attention")).not.toBeInTheDocument();
  });
  it("shows queued handoffs without claiming an acquisition task has started", () => {
    render(
      <HeroUIProvider>
        <AcquisitionHandoffNotice handoff={handoff} hasTaskSummary={false} onChanged={vi.fn()} />
      </HeroUIProvider>,
    );
    expect(screen.getByText("acquire.handoff.status.queued")).toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("keeps a new task accessible until its own task summary has loaded", () => {
    const props = {
      handoff: { ...handoff, status: "created", acquisitionTaskId: 12 },
      onChanged: vi.fn(),
    };
    const view = render(
      <HeroUIProvider>
        <AcquisitionHandoffNotice {...props} hasTaskSummary={false} />
      </HeroUIProvider>,
    );
    expect(screen.getByRole("button", { name: "acquire.handoff.openTask" })).toBeInTheDocument();
    view.rerender(
      <HeroUIProvider>
        <AcquisitionHandoffNotice {...props} hasTaskSummary />
      </HeroUIProvider>,
    );
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
    expect(screen.queryByText("acquire.handoff.status.created")).not.toBeInTheDocument();
  });

  it("only retries handoffs that have not created a task and prevents duplicate retry", async () => {
    let finish!: () => void;
    const onRetry = vi.fn(
      () =>
        new Promise<void>((resolve) => {
          finish = resolve;
        }),
    );
    const props = {
      handoff: { ...handoff, status: "error", code: "HANDOFF_TIMED_OUT" },
      onChanged: vi.fn(),
      onRetry,
    };
    const view = render(
      <HeroUIProvider>
        <AcquisitionHandoffNotice {...props} hasTaskSummary={false} />
      </HeroUIProvider>,
    );
    const user = userEvent.setup();
    const button = screen.getByRole("button", { name: "acquire.handoff.retry" });
    await user.click(button);
    expect(button).toBeDisabled();
    await user.click(button);
    expect(onRetry).toHaveBeenCalledTimes(1);
    await act(async () => finish());
    await waitFor(() => expect(button).not.toBeDisabled());
    view.rerender(
      <HeroUIProvider>
        <AcquisitionHandoffNotice
          {...props}
          handoff={{ ...props.handoff, acquisitionTaskId: 12 }}
          hasTaskSummary={false}
        />
      </HeroUIProvider>,
    );
    expect(screen.queryByRole("button", { name: "acquire.handoff.retry" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "acquire.handoff.openTask" })).toBeInTheDocument();
  });
});
