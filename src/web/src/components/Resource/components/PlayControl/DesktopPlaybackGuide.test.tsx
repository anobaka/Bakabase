import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { DesktopPlaybackGuideButton } from "./DesktopPlaybackGuide";

import { ClientMode } from "@/sdk/constants";

const environment = vi.hoisted(() => ({
  context: "known",
  clientMode: 3,
  appWindow: false,
}));

vi.mock("@/stores/remoteAccess", () => ({
  useRemoteAccessStore: (selector: (value: unknown) => unknown) => selector(environment),
}));
vi.mock("@/utils/clientSurface", () => ({ isDesktopAppWindow: () => environment.appWindow }));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ children, title, onDestroyed }: any) => (
    <div aria-label={title} role="dialog">
      {children}
      <button onClick={onDestroyed}>close guide</button>
    </div>
  ),
  Button: ({ children, onPress }: any) => <button onClick={onPress}>{children}</button>,
}));

beforeEach(() => {
  environment.context = "known";
  environment.clientMode = ClientMode.RemoteBrowser;
  environment.appWindow = false;
});
afterEach(cleanup);

describe("desktop playback guidance", () => {
  it("only opens on request and reuses the download page without changing the current resource", () => {
    render(<DesktopPlaybackGuideButton />);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    fireEvent.click(screen.getByText("resource.desktopPlayback.action"));
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByText("resource.desktopPlayback.connect")).toBeInTheDocument();
    expect(screen.getByText("resource.desktopPlayback.http")).toBeInTheDocument();
    expect(screen.getByText("resource.desktopPlayback.paths")).toBeInTheDocument();
    const download = screen.getByRole("link", { name: "resource.desktopPlayback.download" });

    expect(download).toHaveAttribute("href", "#/other-devices");
    expect(download).toHaveAttribute("target", "_blank");
    expect(download).toHaveAttribute("rel", "noopener noreferrer");
    fireEvent.click(screen.getByText("close guide"));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
  it.each([ClientMode.AllInOne, ClientMode.PureClient])(
    "does not ask a bridged client (%s) to install again",
    (mode) => {
      environment.clientMode = mode;
      render(<DesktopPlaybackGuideButton />);
      expect(screen.queryByRole("button")).not.toBeInTheDocument();
    },
  );
  it.each(["asking", "unknown"])("does not guess during %s access context", (context) => {
    environment.context = context;
    render(<DesktopPlaybackGuideButton />);
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });
  it("never offers a download in a desktop app window even without an available bridge", () => {
    environment.appWindow = true;
    render(<DesktopPlaybackGuideButton />);
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });
});
