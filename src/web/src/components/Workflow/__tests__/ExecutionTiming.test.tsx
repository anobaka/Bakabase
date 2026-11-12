import { render, screen } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import ExecutionTiming, { executionDuration } from "../ExecutionTiming";

vi.mock("react-i18next", () => ({ useTranslation: () => ({
  i18n: { language: "cn" },
  t: (_key: string, options: { duration: string }) => `Elapsed ${options.duration}`,
}) }));

it("uses the server UTC timestamps consistently for times and multi-day elapsed duration", () => {
  const { container } = render(<ExecutionTiming startedAt="2026-11-10 01:02:03.000" completedAt="2026-11-11T04:04:05Z" />);
  expect(screen.getByText("· Elapsed 27:02:02")).toBeVisible();
  expect(Array.from(container.querySelectorAll("time"), node => node.dateTime)).toEqual([
    "2026-11-10T01:02:03.000Z", "2026-11-11T04:04:05.000Z",
  ]);
});

it("shows a same-day end as 24-hour time with its full date still available", () => {
  const { container } = render(<ExecutionTiming startedAt="2026-11-11 01:02:03.000" completedAt="2026-11-11T01:03:05Z" />);
  const times = container.querySelectorAll("time");
  expect(times[0].textContent).toMatch(/2026/);
  expect(times[1].textContent).toMatch(/^\d{2}:\d{2}:\d{2}$/);
  expect(times[1].title).toContain("2026");
  expect(screen.getByText("· Elapsed 00:01:02")).toBeVisible();
});

it("does not invent an end time or duration for an unfinished or invalid attempt", () => {
  const { container, rerender } = render(<ExecutionTiming startedAt="2026-11-10T01:02:03Z" />);
  expect(container.querySelectorAll("time")).toHaveLength(1);
  expect(screen.queryByText(/Elapsed/)).toBeNull();
  rerender(<ExecutionTiming startedAt="invalid" completedAt="2026-11-11T04:04:05Z" />);
  expect(container).toBeEmptyDOMElement();
  expect(executionDuration("2026-11-11T01:02:03Z", "2026-11-10T01:02:03Z")).toBeUndefined();
  expect(executionDuration("2026-11-11T01:02:03Z", "2026-11-11T01:02:03Z")).toBe("00:00:00");
});
