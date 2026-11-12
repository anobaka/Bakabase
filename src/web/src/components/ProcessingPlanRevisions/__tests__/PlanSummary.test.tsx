import { render, screen } from "@testing-library/react";
import { expect, it } from "vitest";
import PlanSummary from "../PlanSummary";

it("shows the exact merged inputs without the ignored legacy reference or saved password", () => {
  const plan = {
    requirement: "required",
    evidence: [],
    steps: [
      {
        id: "unpack",
        op: "extractArchive",
        input: "ignored-legacy-input",
        inputs: ["download", "move-part"],
        password: "private-password-canary",
      },
    ],
  };
  render(<PlanSummary plan={plan} />);
  expect(screen.getByText("download + move-part")).toBeVisible();
  expect(document.body).not.toHaveTextContent("ignored-legacy-input");
  expect(document.body).not.toHaveTextContent("private-password-canary");
});

it("distinguishes an explicit empty replacement from a missing value", () => {
  render(
    <PlanSummary
      plan={{
        requirement: "required",
        evidence: [],
        steps: [
          {
            id: "remove",
            op: "renameLiteral",
            input: "download",
            findText: "literal[1]",
            replaceText: "",
          },
          {
            id: "replace",
            op: "renameLiteral",
            input: "remove",
            findText: "Before",
            replaceText: "After",
          },
        ],
      }}
    />,
  );
  expect(screen.getByText("literal[1]")).toBeVisible();
  expect(screen.getByText("acquisition.plan.step.emptyReplacement")).toBeVisible();
  expect(screen.getByText("Before")).toBeVisible();
  expect(screen.getByText("After")).toBeVisible();
});
