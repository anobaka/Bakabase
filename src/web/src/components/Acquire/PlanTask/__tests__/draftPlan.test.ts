import { expect, it } from "vitest";
import { draftPlanFromRequest } from "../draftPlan";

it("turns only explicit sources into independent parts without guessing extraction", () => {
  const plan = draftPlanFromRequest(
    {
      mode: "files",
      title: "Reviewed title",
      candidates: [
        {
          id: "a",
          url: "https://example.com/a",
          kind: "auto",
          methodId: "http",
          accountId: "chosen",
          accessCode: "code",
        },
        { id: "b", url: "https://example.com/b", kind: "auto" },
      ],
      selectedCandidateIds: ["a"],
    },
    "/chosen",
    "Original",
  );
  expect(plan.title).toBe("Reviewed title");
  expect(plan.outputDirectory).toBe("/chosen");
  expect(plan.delivery).toBe("files");
  expect(plan.parts).toHaveLength(1);
  expect(plan.parts[0].sources[0]).toMatchObject({
    id: "a",
    methodId: "http",
    accountId: "chosen",
    accessCode: "code",
  });
  expect(plan.parts[0].processing).toEqual({ requirement: "unknown", steps: [], evidence: [] });
});
it("preserves an explicitly reviewed processing plan and rejects an unrelated selection", () => {
  const request = {
    mode: "files" as const,
    candidates: [
      {
        id: "a",
        url: "https://example.com",
        kind: "auto",
        extractionPlanJson: JSON.stringify({
          requirement: "notRequired",
          steps: [],
          evidence: ["manual"],
        }),
      },
    ],
    selectedCandidateIds: ["a"],
  };
  expect(draftPlanFromRequest(request, "/chosen", "Name").parts[0].processing?.requirement).toBe(
    "notRequired",
  );
  expect(() =>
    draftPlanFromRequest({ ...request, selectedCandidateIds: ["missing"] }, "/chosen", "Name"),
  ).toThrow("SELECTION_INVALID");
});
