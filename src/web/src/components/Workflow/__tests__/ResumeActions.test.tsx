import { Children, type ReactNode } from "react";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import TaskContinuationModal from "@/components/Acquire/PlanTask/TaskContinuationModal";
import ResumeActions, { ResumeActionsGuard } from "../ResumeActions";

vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ title, children, footer }: any) => <div role="dialog" aria-label={title}>{children}<footer>{footer}</footer></div>,
  Button: ({ children, isDisabled, onPress }: any) => <button disabled={isDisabled} onClick={onPress}>{children}</button>,
  Tabs: ({ children, onSelectionChange, isDisabled }: any) => <div>{Children.map(children, (child: any) =>
    <button disabled={isDisabled} onClick={() => onSelectionChange(child.key)}>{child.props.title}</button>)}</div>,
  Tab: () => null,
}));

function Actions({ label, busy = false, guard = false }: { label: string; busy?: boolean; guard?: boolean }) {
  return <ResumeActionsGuard disabled={guard}>
    <ResumeActions busy={busy} cancel={<button>Internal cancel</button>}><button>{label}</button></ResumeActions>
  </ResumeActionsGuard>;
}

it("routes to one form without tabs, keeping only its footer action and preserving stale and preview guards", () => {
  const props = { onClose: vi.fn(), current: <Actions label="Retry original" guard />, files: <Actions label="Process files" /> };
  const view = render(<TaskContinuationModal {...props} stale={false} />);
  const footer = within(screen.getByRole("dialog").querySelector("footer")!);
  expect(footer.getByRole("button", { name: "Retry original" })).toBeDisabled();
  expect(screen.queryByText("Internal cancel")).toBeNull();
  expect(screen.queryByRole("button", { name: "acquisition.continue.files" })).toBeNull();
  view.rerender(<TaskContinuationModal {...props} stale={false} preferFiles />);
  expect(footer.queryByRole("button", { name: "Retry original" })).toBeNull();
  expect(footer.getByRole("button", { name: "Process files" })).toBeEnabled();
  view.rerender(<TaskContinuationModal {...props} stale={false} />);
  expect(footer.queryByRole("button", { name: "Process files" })).toBeNull();
  view.rerender(<TaskContinuationModal {...props} current={<Actions label="Retry original" />} stale />);
  expect(footer.getByRole("button", { name: "Retry original" })).toBeDisabled();
});

it("keeps the outer close disabled while its active form submits and preserves standalone cancellation", () => {
  const props = { onClose: vi.fn(), stale: false };
  const view = render(<TaskContinuationModal {...props} files={<Actions label="Process" busy />} />);
  expect(screen.getByRole("button", { name: "acquisition.plan.close" })).toBeDisabled();
  view.rerender(<TaskContinuationModal {...props} files={<Actions label="Process" />} />);
  expect(screen.getByRole("button", { name: "acquisition.plan.close" })).toBeEnabled();
  view.unmount();
  render(<Actions label="Standalone" />);
  expect(screen.getByRole("button", { name: "Internal cancel" })).toBeVisible();
});
