import { beforeEach, expect, it, vi } from "vitest";
import { querySourceProcessingPlan } from "../api";
import { planTaskApi } from "@/components/Acquire/PlanTask/api";
const mocks = vi.hoisted(() => ({ query: vi.fn(), apply: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({
  default: {
    processingPlans: { getSourceProcessingPlan: mocks.query },
    acquisition: { applyAcquisitionProcessingRevisions: mocks.apply },
  },
}));
const source = {
  module: "postParser",
  entityKind: "task",
  entityId: "41",
  revision: "1",
  resultKey: "0",
};
beforeEach(() => {
  vi.clearAllMocks();
});
it("marks source plans as private responses and uses the complete source identity", async () => {
  mocks.query.mockResolvedValue({ code: 0, data: { source } });
  await querySourceProcessingPlan(source);
  expect(mocks.query).toHaveBeenCalledExactlyOnceWith(source, {
    sensitiveResponse: true,
    showErrorToast: false,
  });
});
it("marks applied plan responses as private and sends both acquisition and source revision identities", async () => {
  mocks.apply.mockResolvedValue({ code: 0, data: { task: { id: 3 } } });
  await planTaskApi.applyProcessingRevision(3, 2, "part-a", source, "revision-a");
  expect(mocks.apply).toHaveBeenCalledExactlyOnceWith(
    3,
    { expectedRevision: 2, items: [{ partId: "part-a", source, revisionId: "revision-a" }] },
    { sensitiveResponse: true, showErrorToast: false },
  );
});
