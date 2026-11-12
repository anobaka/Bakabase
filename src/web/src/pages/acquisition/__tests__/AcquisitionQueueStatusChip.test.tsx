import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import AcquisitionQueueStatusChip, { acquisitionQueueStatusColor } from "../components/AcquisitionQueueStatusChip";

vi.mock("@/components/bakaui", () => ({
  Chip: ({ children, color, startContent }: any) => (
    <span data-color={color}>{startContent}{children}</span>
  ),
  Spinner: ({ "aria-hidden": ariaHidden }: any) => <span role="status" aria-hidden={ariaHidden} />,
}));

afterEach(() => cleanup());

it.each([
  ["completed", "success"],
  ["attention", "warning"],
  ["spaceBlocked", "warning"],
  ["failed", "danger"],
  ["unknown", "danger"],
  ["queued", "primary"],
])("maps %s to the %s semantic color", (status, color) => {
  expect(acquisitionQueueStatusColor(status)).toBe(color);
});

it("shows a loading indicator for active processing states", () => {
  render(<AcquisitionQueueStatusChip status="running">处理中</AcquisitionQueueStatusChip>);
  expect(screen.getByRole("status", { hidden: true })).toBeVisible();
  expect(screen.getByText("处理中").closest("[data-color]")).toHaveAttribute("data-color", "primary");
});
