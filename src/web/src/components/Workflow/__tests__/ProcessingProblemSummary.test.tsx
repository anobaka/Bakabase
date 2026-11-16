import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { HeroUIProvider } from "@heroui/react";
import { expect, it, vi } from "vitest";
import ProcessingProblemSummary from "../ProcessingProblemSummary";

vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));

it("prioritizes a classified archive problem and exposes executor diagnostics on keyboard focus", async () => {
  const user = userEvent.setup();
  const raw = "Could not open input.7z. Check the password and all archive parts.";
  render(<ProcessingProblemSummary reason="PasswordUnknown" summary="Check the archive password and all volumes."
    message={raw} />, { wrapper: HeroUIProvider });
  expect(screen.getByText("Check the archive password and all volumes.")).toBeVisible();
  expect(screen.queryByText(raw)).toBeNull();
  const info = screen.getByRole("button", { name: "workflow.processing.problem.details" });
  await user.tab();
  expect(info).toHaveFocus();
  expect(await screen.findByRole("tooltip")).toHaveTextContent(raw);
  await user.keyboard("{Escape}");
  expect(screen.queryByRole("tooltip")).toBeNull();
});

it.each([undefined, "FutureProcessingFailure", "ExtractionPlanUnknown", "FILE_DELIVERY_RESULT_UNCERTAIN"])(
  "keeps concrete unknown or review text visible for %s instead of hiding it behind a generic category", reason => {
    const message = "The expected resource has no usable image files. <script>diagnostic</script>";
    const view = render(<ProcessingProblemSummary reason={reason} summary="Review this result." message={message} />);
    expect(screen.getByText(message)).toBeVisible();
    expect(screen.queryByRole("button")).toBeNull();
    expect(view.container.querySelector("script")).toBeNull();
  },
);

it("keeps raw details visible if a known code has no readable translation", () => {
  render(<ProcessingProblemSummary reason="PasswordUnknown" summary="PasswordUnknown" message="Concrete executor diagnostic" />);
  expect(screen.getByText("Concrete executor diagnostic")).toBeVisible();
  expect(screen.queryByRole("button")).toBeNull();
});
