import { render, screen } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import AcquisitionTiming from "../AcquisitionTiming";
import { AcquisitionStatus } from "@/sdk/constants";
vi.mock("react-i18next", () => ({
  useTranslation: () => ({ t: (key: string) => key, i18n: { language: "cn" } }),
}));
const createdAt = "2026-10-21T01:00:00Z";
it("shows creation time and an explicit not-started state for a prepared task", () => {
  const view = render(
    <AcquisitionTiming task={{ createdAt, status: AcquisitionStatus.Waiting }} />,
  );
  expect(view.container.querySelector("time")?.getAttribute("datetime")).toBe(createdAt);
  expect(screen.getByText("acquisition.timing.notStarted")).toBeVisible();
  expect(screen.queryByText("acquisition.timing.notEnded")).toBeNull();
});
it("does not call a missing historical execution timestamp not-started", () => {
  render(
    <AcquisitionTiming
      task={{ createdAt, workflowRunId: 99, status: AcquisitionStatus.Completed }}
    />,
  );
  expect(screen.getAllByText("acquisition.timing.unavailable")).toHaveLength(2);
  expect(screen.queryByText("acquisition.timing.notStarted")).toBeNull();
});
it("uses recorded execution start and end without implying active processing duration", () => {
  const startedAt = "2026-10-21T02:00:00Z";
  const endedAt = "2026-10-21T03:00:00Z";
  const view = render(
    <AcquisitionTiming
      task={{
        createdAt,
        workflowRunId: 99,
        status: AcquisitionStatus.Completed,
        executionStartedAt: startedAt,
        executionCompletedAt: endedAt,
      }}
    />,
  );
  expect(
    [...view.container.querySelectorAll("time")].map((element) => element.getAttribute("datetime")),
  ).toEqual([createdAt, startedAt, endedAt]);
  expect(screen.queryByText("acquisition.timing.notEnded")).toBeNull();
  view.rerender(
    <AcquisitionTiming
      task={{
        createdAt,
        workflowRunId: 99,
        status: AcquisitionStatus.Waiting,
        executionStartedAt: startedAt,
      }}
    />,
  );
  expect(screen.getByText("acquisition.timing.notEnded")).toBeVisible();
});
it("does not invent an end time for a failed execution or show malformed dates", () => {
  const view = render(
    <AcquisitionTiming
      task={{
        createdAt: "invalid",
        workflowRunId: 99,
        status: AcquisitionStatus.Failed,
        executionStartedAt: "2026-10-21T02:00:00Z",
      }}
    />,
  );
  expect(screen.getAllByText("acquisition.timing.unavailable")).toHaveLength(2);
  expect(view.container.textContent).not.toContain("Invalid Date");
  expect(screen.queryByText("acquisition.timing.notEnded")).toBeNull();
});
