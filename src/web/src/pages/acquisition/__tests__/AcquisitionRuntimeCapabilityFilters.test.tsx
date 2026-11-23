import { act, render, screen } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import { HeroUIProvider } from "@heroui/react";
import AcquisitionRuntimeCapabilityFilters from "../components/AcquisitionRuntimeCapabilityFilters";

it("renders provider-owned capability and execution-state facets without a hard-coded option list", async () => {
  const onCapabilityChange = vi.fn();
  const onExecutionStateChange = vi.fn();
  render(
    <HeroUIProvider disableAnimation>
      <AcquisitionRuntimeCapabilityFilters
        capabilityCounts={{ "custom.archive": 3 }}
        executionStateCounts={{ running: 3 }}
        capabilities={[{ capabilityId: "custom.archive", capabilityName: "Archive worker" }]}
        capabilityId={undefined}
        executionState={undefined}
        onCapabilityChange={onCapabilityChange}
        onExecutionStateChange={onExecutionStateChange}
      />
    </HeroUIProvider>,
  );
  const capability = screen.getByRole("button", { name: "Archive worker 3" });
  expect(capability).toHaveAttribute("aria-pressed", "false");
  await act(async () => capability.click());
  expect(onCapabilityChange).toHaveBeenLastCalledWith("custom.archive");
  await act(async () => screen.getByRole("button", { name: /Running 3/ }).click());
  expect(onExecutionStateChange).toHaveBeenLastCalledWith("running");
});
