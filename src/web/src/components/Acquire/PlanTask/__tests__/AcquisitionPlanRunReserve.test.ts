import { beforeEach, expect, it, vi } from "vitest";
import { planTaskApi } from "../api";

const run = vi.hoisted(() => vi.fn());
vi.mock("@/sdk/BApi", () => ({ default: { acquisition: { runAcquisitionPlan: run } } }));
beforeEach(() => { run.mockReset(); run.mockResolvedValue({code:0,data:{id:42}}); });

it.each([true,false])("serializes explicit run-only reserve %s without dropping false", async (value) => {
  await planTaskApi.run(42,7,value);
  expect(run).toHaveBeenCalledExactlyOnceWith(42,{expectedRevision:7,useEmergencySpaceReserve:value});
});
it("keeps an omitted choice distinct from explicit false", async () => {
  await planTaskApi.run(42,7);
  expect(run).toHaveBeenCalledExactlyOnceWith(42,{expectedRevision:7});
});
