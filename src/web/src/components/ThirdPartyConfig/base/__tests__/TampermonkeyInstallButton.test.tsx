import type { ReactNode } from "react";

import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import TampermonkeyInstallButton from "../TampermonkeyInstallButton";

import { ClientMode } from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

const { install, scriptUrl, nativeOpen } = vi.hoisted(() => ({
  install: vi.fn(),
  scriptUrl: vi.fn(),
  nativeOpen: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    tampermonkey: { installTampermonkeyScript: install, getTampermonkeyScriptUrl: scriptUrl },
    gui: { openUrlInDefaultBrowser: nativeOpen },
  },
}));
vi.mock("@/components/bakaui", () => ({
  Alert: ({ title, description }: { title: ReactNode; description: ReactNode }) => (
    <div>
      {title}
      {description}
    </div>
  ),
  Button: ({ children, onPress }: { children: ReactNode; onPress: () => void }) => (
    <button onClick={onPress}>{children}</button>
  ),
}));

const initial = useRemoteAccessStore.getState();
const scriptPath = "/Tampermonkey/script/bakabase.user.js";

beforeEach(() => {
  vi.clearAllMocks();
  install.mockResolvedValue({ code: 0 });
  scriptUrl.mockReturnValue(scriptPath);
  useRemoteAccessStore.setState({ initialized: true, context: "known" });
});
afterEach(() => {
  cleanup();
  useRemoteAccessStore.setState(initial, true);
  vi.restoreAllMocks();
});

function captureNavigation() {
  const opened: { href: string; target: string; rel: string }[] = [];

  vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function (
    this: HTMLAnchorElement,
  ) {
    opened.push({ href: this.href, target: this.target, rel: this.rel });
  });

  return opened;
}

describe("userscript installation", () => {
  it.each([true, false])(
    "opens the script synchronously in a server browser (isLocal=%s)",
    (isLocal) => {
      useRemoteAccessStore.setState({ clientMode: ClientMode.RemoteBrowser, isLocal });
      const opened = captureNavigation();

      render(<TampermonkeyInstallButton />);
      fireEvent.click(
        screen.getByRole("button", { name: "thirdPartyIntegration.action.oneClickInstall" }),
      );
      const expected = new URL(scriptPath, window.location.origin);

      expected.searchParams.set("apiEndpoint", window.location.origin);
      expect(opened).toEqual([
        { href: expected.href, target: "_blank", rel: "noopener noreferrer" },
      ]);
      expect(install).not.toHaveBeenCalled();
      expect(nativeOpen).not.toHaveBeenCalled();
    },
  );

  it.each([
    ["http://192.168.3.23:34567", "http://192.168.3.23:34567"],
    ["https://bakabase.example", "https://bakabase.example"],
    ["//api.example:8443", `${window.location.protocol}//api.example:8443`],
  ])("uses the actual API origin %s instead of the page origin", (endpoint, origin) => {
    useRemoteAccessStore.setState({ clientMode: ClientMode.RemoteBrowser });
    scriptUrl.mockReturnValue(`${endpoint}${scriptPath}`);
    const opened = captureNavigation();

    render(<TampermonkeyInstallButton />);
    fireEvent.click(screen.getByRole("button"));
    const url = new URL(opened[0].href);

    expect(url.origin).toBe(origin);
    expect(url.pathname).toBe(scriptPath);
    expect(url.searchParams.get("apiEndpoint")).toBe(origin);
    expect(install).not.toHaveBeenCalled();
  });

  it.each([ClientMode.AllInOne, ClientMode.PureClient])(
    "retains desktop installation in mode %s",
    (clientMode) => {
      useRemoteAccessStore.setState({ clientMode, isLocal: clientMode === ClientMode.AllInOne });
      const opened = captureNavigation();

      render(<TampermonkeyInstallButton />);
      fireEvent.click(screen.getByRole("button"));
      expect(install).toHaveBeenCalledExactlyOnceWith();
      expect(scriptUrl).not.toHaveBeenCalled();
      expect(nativeOpen).not.toHaveBeenCalled();
      expect(opened).toEqual([]);
    },
  );
});
