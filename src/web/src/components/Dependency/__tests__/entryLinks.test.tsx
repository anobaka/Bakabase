import type { ReactNode } from "react";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import FfMpegRequired from "@/pages/downloader/components/TaskDetailModal/components/FfMpegRequired";
import { LeStatusIndicator } from "@/pages/dlsite-works/components/LeStatusIndicator";
import ids from "@/core/models/Constants/DependentComponentIds";
import { DependentComponentStatus } from "@/sdk/constants";

const mocks = vi.hoisted(() => ({
  contexts: [] as { id: string; status: number; isAvailableOnCurrentPlatform?: boolean }[],
  install: vi.fn(),
}));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/stores/dependentComponentContexts", () => ({
  useDependentComponentContextsStore: (
    select: (state: { contexts: typeof mocks.contexts }) => unknown,
  ) => select({ contexts: mocks.contexts }),
}));
vi.mock("@/sdk/BApi", () => ({
  default: { component: { installDependentComponent: mocks.install } },
}));
vi.mock("@/components/bakaui", () => ({
  Alert: ({ title }: { title: ReactNode }) => <div role="alert">{title}</div>,
}));
vi.mock("@heroui/react", () => ({
  Button: ({ onPress, children }: { onPress?: () => void; children: ReactNode }) => (
    <button onClick={onPress}>{children}</button>
  ),
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  Spinner: () => null,
}));
vi.mock("@/components/bakaui/components/Tooltip", () => ({
  default: ({ children }: { children: ReactNode }) => children,
}));

beforeEach(() => {
  mocks.contexts = [];
  mocks.install.mockResolvedValue({ code: 0 });
});
afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe("feature dependency entry links", () => {
  it("targets the FFmpeg component when required and hides the hint after installation", () => {
    const { rerender } = render(<FfMpegRequired />);
    expect(screen.getByRole("link", { name: "menu.dependencies" })).toHaveAttribute(
      "href",
      `#/configuration?tab=dependency&component=${ids.FFMpeg}`,
    );
    mocks.contexts = [{ id: ids.FFMpeg, status: DependentComponentStatus.Installed }];
    rerender(<FfMpegRequired />);
    expect(screen.queryByRole("link")).toBeNull();
  });

  it("keeps Locale Emulator installation in place and offers its dependency details", () => {
    render(<LeStatusIndicator />);
    expect(screen.getByRole("link", { name: "menu.dependencies" })).toHaveAttribute(
      "href",
      `#/configuration?tab=dependency&component=${ids.LocaleEmulator}`,
    );
    fireEvent.click(screen.getByRole("button", { name: "resourceSource.dlsite.le.install" }));
    expect(mocks.install).toHaveBeenCalledExactlyOnceWith(ids.LocaleEmulator);
  });
});
