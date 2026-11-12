import { fireEvent, render, screen } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import { ResumeProblem } from "../ResumePresentation";
import cn from "@/locales/cn/pages/cloudDownload.json";

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: { defaultValue?: string }) =>
      (cn as Record<string, string>)[key] ?? options?.defaultValue ?? key,
  }),
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, ...props }: any) => (
    <button aria-expanded={props["aria-expanded"]} onClick={onPress}>
      {children}
    </button>
  ),
}));

it("shows a localized provider reason and only an allowlisted code in technical details", () => {
  render(
    <ResumeProblem
      problem="CloudProviderException: PIKPAK_ORIGINAL_FILE_UNAVAILABLE\n at PrivateStack SECRET_TOKEN_VALUE"
      fallback="Cannot continue"
    />,
  );
  expect(
    screen.getByText(cn["cloudDownload.problemCode.PIKPAK_ORIGINAL_FILE_UNAVAILABLE"]),
  ).toBeVisible();
  expect(screen.queryByText("PIKPAK_ORIGINAL_FILE_UNAVAILABLE")).toBeNull();
  fireEvent.click(screen.getByRole("button"));
  expect(screen.getByText("PIKPAK_ORIGINAL_FILE_UNAVAILABLE")).toBeVisible();
  expect(screen.queryByText(/PrivateStack|SECRET_TOKEN_VALUE/)).toBeNull();
});

it("uses the actionable fallback for unknown exceptions without disclosing arbitrary diagnostic tokens", () => {
  render(
    <ResumeProblem
      problem="Unexpected exception UNKNOWN_PRIVATE_PAYLOAD"
      fallback="Review the selected source"
    />,
  );
  expect(screen.getByText("Review the selected source")).toBeVisible();
  expect(screen.queryByRole("button")).toBeNull();
  expect(screen.queryByText(/UNKNOWN_PRIVATE_PAYLOAD/)).toBeNull();
});

it("asks for a valid replacement share and retains only the cancelled code in recovery details", () => {
  render(
    <ResumeProblem
      problem="CloudProviderException: BAIDU_SHARE_CANCELLED\n at PrivateStack SECRET_TOKEN_VALUE"
      fallback="Review the selected source"
    />,
  );
  expect(screen.getByText(cn["cloudDownload.problemCode.BAIDU_SHARE_CANCELLED"])).toBeVisible();
  expect(screen.queryByText(/提取码/)).toBeNull();
  fireEvent.click(screen.getByRole("button"));
  expect(screen.getByText("BAIDU_SHARE_CANCELLED")).toBeVisible();
  expect(screen.queryByText(/PrivateStack|SECRET_TOKEN_VALUE/)).toBeNull();
});
