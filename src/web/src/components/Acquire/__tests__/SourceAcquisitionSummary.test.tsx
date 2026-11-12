import type { ReactNode } from "react";
import type { BakabaseModulesAcquisitionAbstractionsModelsDomainAcquisitionTask as Task } from "@/sdk/Api";
import { render, screen } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import SourceAcquisitionSummary from "../SourceAcquisitionSummary";
import { AcquisitionStatus } from "@/sdk/constants";
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: { phase?: string }) => options?.phase ?? key,
  }),
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children }: { children: ReactNode }) => <button>{children}</button>,
  Spinner: () => null,
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
it.each([AcquisitionStatus.Cancelled, AcquisitionStatus.Failed, AcquisitionStatus.Completed])(
  "does not present terminal status %s as an actionable attention phase",
  (status) => {
    const task = { id: 1, status, phase: "attention" } as Task;
    render(<SourceAcquisitionSummary tasks={[task]} onChanged={() => {}} />);
    expect(screen.getByRole("button").textContent).not.toContain(
      "acquisition.plan.phase.attention",
    );
    expect(screen.getByRole("button").textContent).toContain(
      AcquisitionStatus[status].toLowerCase(),
    );
  },
);

it("labels a deleted source association as retained history instead of unresolved work", () => {
  const task = {
    id: 1,
    status: AcquisitionStatus.Waiting,
    phase: "attention",
    isDeleted: true,
  } as Task;
  render(<SourceAcquisitionSummary tasks={[task]} onChanged={() => {}} />);
  expect(screen.getByRole("button")).toHaveTextContent("acquisition.delete.deletedHistory");
  expect(screen.getByRole("button").textContent).not.toContain("acquisition.plan.phase.attention");
});
