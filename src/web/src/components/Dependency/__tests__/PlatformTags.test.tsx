import type { ReactNode } from "react";
import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import PlatformTags from "../PlatformTags";
import type { DependencyContext } from "../presentation";
import { DependentComponentStatus } from "@/sdk/constants";

vi.mock("@/components/bakaui", () => ({
  Chip: ({ children, title }: { children: ReactNode; title?: string }) => (
    <span title={title}>{children}</span>
  ),
}));
const context: DependencyContext = {
  id: "tool",
  name: "Tool",
  defaultLocation: "/components/tool",
  status: DependentComponentStatus.Installed,
  isRequired: false,
  installationProgress: 0,
  isAvailableOnCurrentPlatform: true,
};
describe("dependency platform declarations", () => {
  it("renders each backend-declared OS once while retaining architecture and runtime details", () => {
    render(
      <PlatformTags
        context={{
          ...context,
          currentOs: "linux",
          supportedTargets: [
            { os: "windows", architecture: "x64", canAutoInstall: true },
            { os: "windows", architecture: "arm64", canAutoInstall: false },
            {
              os: "linux",
              architecture: "x64",
              runtime: "glibc",
              minimumVersion: "2.28",
              canAutoInstall: true,
            },
          ],
        }}
      />,
    );
    expect(screen.getAllByText("dependencies.os.windows")).toHaveLength(1);
    expect(screen.getByText("dependencies.os.linux")).toHaveAttribute(
      "title",
      expect.stringContaining("glibc"),
    );
    expect(screen.getByText("dependencies.os.windows")).toHaveAttribute(
      "title",
      expect.stringContaining("arm64"),
    );
    expect(screen.queryByText("dependencies.os.macos")).toBeNull();
  });
  it("does not guess a support matrix from the current-platform availability flag", () => {
    const { container } = render(<PlatformTags context={context} />);
    expect(container).toBeEmptyDOMElement();
  });
});
