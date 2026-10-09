import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ExternalLink from "..";

import { ClientMode } from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";
import { openExternalUrl } from "@/utils/openExternalUrl";

const nativeOpen = vi.hoisted(() => vi.fn());

vi.mock("@/sdk/BApi", () => ({
  default: { gui: { openUrlInDefaultBrowser: nativeOpen } },
}));

const initialState = useRemoteAccessStore.getState();
const url = "https://github.com/anobaka/Bakabase";

beforeEach(() => {
  nativeOpen.mockClear();
  useRemoteAccessStore.setState({ initialized: true, context: "known" });
});

afterEach(() => {
  cleanup();
  useRemoteAccessStore.setState(initialState, true);
  vi.restoreAllMocks();
});

describe("external web links", () => {
  it.each([true, false])(
    "uses a normal new-tab link in a server browser (isLocal=%s)",
    (isLocal) => {
      useRemoteAccessStore.setState({ clientMode: ClientMode.RemoteBrowser, isLocal });
      render(<ExternalLink href={url}>GitHub</ExternalLink>);
      const link = screen.getByRole("link", { name: "GitHub" });

      expect(link).toHaveAttribute("href", url);
      expect(link).toHaveAttribute("target", "_blank");
      expect(link).toHaveAttribute("rel", "noopener noreferrer");
      // Cancel jsdom's navigation, while still exercising the component's event handling.
      link.addEventListener("click", (event) => event.preventDefault(), { once: true });
      fireEvent.click(link);
      expect(nativeOpen).not.toHaveBeenCalled();
    },
  );

  it.each([ClientMode.AllInOne, ClientMode.PureClient])(
    "keeps the native opener for desktop mode %s",
    async (clientMode) => {
      useRemoteAccessStore.setState({ clientMode, isLocal: clientMode === ClientMode.AllInOne });
      render(<ExternalLink href={url}>GitHub</ExternalLink>);
      const link = screen.getByRole("link", { name: "GitHub" });

      expect(link).not.toHaveAttribute("href");
      await userEvent.click(link);
      expect(nativeOpen).toHaveBeenCalledExactlyOnceWith({ url });
    },
  );

  it.each([
    "javascript:alert(1)",
    "data:text/html,test",
    "file:///etc/passwd",
    "/relative",
    "bad url",
  ])("does not expose an unsafe browser link: %s", (href) => {
    useRemoteAccessStore.setState({ clientMode: ClientMode.RemoteBrowser });
    render(<ExternalLink href={href}>Unsafe</ExternalLink>);
    const link = screen.getByText("Unsafe").closest("a");

    expect(link).not.toHaveAttribute("href");
    expect(link).toHaveAttribute("aria-disabled", "true");
    expect(nativeOpen).not.toHaveBeenCalled();
  });
});

describe("button and menu external actions", () => {
  it.each([true, false])("opens synchronously in a server browser (isLocal=%s)", (isLocal) => {
    useRemoteAccessStore.setState({ clientMode: ClientMode.RemoteBrowser, isLocal });
    let opened: { href: string; target: string; rel: string; connected: boolean } | undefined;
    const click = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      opened = { href: this.href, target: this.target, rel: this.rel, connected: this.isConnected };
    });

    openExternalUrl(url);

    expect(opened).toEqual({
      href: url,
      target: "_blank",
      rel: "noopener noreferrer",
      connected: true,
    });
    expect(click).toHaveBeenCalledOnce();
    expect(document.querySelector("a")).toBeNull();
    expect(nativeOpen).not.toHaveBeenCalled();
  });

  it.each([ClientMode.AllInOne, ClientMode.PureClient])("keeps native mode %s", (clientMode) => {
    useRemoteAccessStore.setState({ clientMode });
    const click = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});

    openExternalUrl(url);

    expect(nativeOpen).toHaveBeenCalledExactlyOnceWith({ url });
    expect(click).not.toHaveBeenCalled();
  });

  it("rejects invalid browser schemes before creating a navigation", () => {
    useRemoteAccessStore.setState({ clientMode: ClientMode.RemoteBrowser });
    const click = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});

    for (const value of [
      "javascript:alert(1)",
      "file:///etc/passwd",
      "data:text/plain,test",
      "bad url",
    ]) {
      openExternalUrl(value);
    }

    expect(click).not.toHaveBeenCalled();
    expect(nativeOpen).not.toHaveBeenCalled();
  });
});
