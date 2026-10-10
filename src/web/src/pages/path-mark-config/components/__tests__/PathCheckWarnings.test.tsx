import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";

import PathCheckWarnings from "../PathCheckWarnings";

vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress }: any) => <button onClick={onPress}>{children}</button>,
}));
afterEach(cleanup);

it("shows the failed path and reason with retry and mark-management actions", () => {
  const retry = vi.fn();
  const manage = vi.fn();

  render(
    <PathCheckWarnings
      checking={false}
      errors={new Map([["E:/Downloads", "Choose a mounted storage location"]])}
      onManage={manage}
      onRetry={retry}
    />,
  );
  expect(screen.getByRole("alert")).toHaveTextContent(
    "E:/Downloads: Choose a mounted storage location",
  );
  fireEvent.click(screen.getByText("pathMarkConfig.pathCheck.retry"));
  fireEvent.click(screen.getByText("pathMarkConfig.pathCheck.managePaths"));
  expect(retry).toHaveBeenCalledTimes(1);
  expect(manage).toHaveBeenCalledTimes(1);
});
