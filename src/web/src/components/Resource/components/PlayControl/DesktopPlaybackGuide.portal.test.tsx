import { HeroUIProvider } from "@heroui/react";
import { cleanup, createEvent, fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { DesktopPlaybackGuide } from "./DesktopPlaybackGuide";

vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/stores/remoteAccess", () => ({ useRemoteAccessStore: () => false }));

afterEach(cleanup);

describe("desktop guide inside a resource menu portal", () => {
  it("preserves disclosure and download defaults when its React ancestor cancels clicks", async () => {
    const cancelClick = vi.fn((event: React.MouseEvent) => event.preventDefault());
    const cancelActivation = vi.fn((event: React.KeyboardEvent) => {
      if (event.key === "Enter" || event.key === " ") event.preventDefault();
    });
    const close = vi.fn();
    const { container } = render(
      <HeroUIProvider disableAnimation>
        <div role="presentation" onClick={cancelClick} onKeyDown={cancelActivation}>
          <DesktopPlaybackGuide onClose={close} />
        </div>
      </HeroUIProvider>,
    );
    const dialog = await screen.findByRole("dialog");

    // Use the real HeroUI modal: its portal is outside the ancestor in the DOM,
    // but unisolated React events still bubble to that ancestor.
    expect(container.contains(dialog)).toBe(false);
    const summary = screen.getByText("resource.desktopPlayback.requirements");
    const details = summary.closest("details")!;

    expect(details.open).toBe(false);
    await userEvent.click(summary);
    expect(details.open).toBe(true);
    await userEvent.click(summary);
    expect(details.open).toBe(false);
    for (const key of ["Enter", " "]) {
      const activation = createEvent.keyDown(summary, { key, bubbles: true, cancelable: true });

      fireEvent(summary, activation);
      expect(activation.defaultPrevented).toBe(false);
    }
    const download = screen.getByRole("link", { name: "resource.desktopPlayback.download" });
    const click = createEvent.click(download, { bubbles: true, cancelable: true });

    fireEvent(download, click);
    expect(click.defaultPrevented).toBe(false);
    expect(download).toHaveAttribute("href", "#/other-devices");
    expect(download).toHaveAttribute("target", "_blank");
    expect(cancelClick).not.toHaveBeenCalled();
    expect(cancelActivation).not.toHaveBeenCalled();
    expect(close).not.toHaveBeenCalled();
  });
});
