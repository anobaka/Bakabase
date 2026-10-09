import { HeroUIProvider } from "@heroui/react";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ThirdPartyConfigurationPage from "..";

vi.mock("@/components/ThirdPartyIcon", () => ({
  default: () => <span data-testid="source-icon" />,
}));
vi.mock("@/components/ThirdPartyConfig", () => {
  const panel = (name: string) => () => <input aria-label={`${name} setting`} defaultValue="" />;

  return Object.fromEntries(
    [
      "AvSources",
      "Bangumi",
      "Bilibili",
      "Cien",
      "DLsite",
      "ExHentai",
      "Fanbox",
      "Fantia",
      "Patreon",
      "Pixiv",
      "SoulPlus",
      "Steam",
      "Tmdb",
    ].map((name) => [`${name}ConfigPanel`, panel(name)]),
  );
});

const storageKey = "thirdPartyConfig.selectedTab";
const originalWidth = window.innerWidth;
const renderPage = () =>
  render(
    <HeroUIProvider disableAnimation>
      <ThirdPartyConfigurationPage />
    </HeroUIProvider>,
  );

describe("third-party configuration navigation", () => {
  beforeEach(() => {
    localStorage.clear();
    vi.stubGlobal(
      "ResizeObserver",
      class {
        observe() {}
        unobserve() {}
        disconnect() {}
      },
    );
  });
  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
    Object.defineProperty(window, "innerWidth", { configurable: true, value: originalWidth });
  });

  it("restores the saved source and exposes only its configuration panel", () => {
    localStorage.setItem(storageKey, "pixiv");
    renderPage();

    expect(screen.getByRole("tab", { name: "Pixiv" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getAllByRole("tabpanel")).toHaveLength(1);
    expect(screen.getByRole("textbox", { name: "Pixiv setting" })).toBeInTheDocument();
    expect(screen.getByRole("tablist")).toHaveAccessibleName(
      "thirdPartyConfig.navigation.source.label",
    );
    expect(screen.getByRole("tablist")).toHaveAttribute("aria-orientation", "vertical");
  });

  it("falls back from a stale stored source and persists full-row tab selection", async () => {
    localStorage.setItem(storageKey, "removed-source");
    const user = userEvent.setup();

    renderPage();

    expect(screen.getByRole("textbox", { name: "Bilibili setting" })).toBeInTheDocument();
    await user.click(screen.getByRole("tab", { name: "Steam" }));
    expect(screen.getByRole("textbox", { name: "Steam setting" })).toBeInTheDocument();
    expect(localStorage.getItem(storageKey)).toBe("steam");
  });

  it("supports vertical keyboard navigation and syncs the compact source selector", async () => {
    const user = userEvent.setup();

    renderPage();
    act(() => screen.getByRole("tab", { name: "Bilibili" }).focus());
    await user.keyboard("{ArrowDown}");

    expect(screen.getByRole("tab", { name: "ExHentai" })).toHaveFocus();
    expect(screen.getByRole("textbox", { name: "ExHentai setting" })).toBeInTheDocument();
    expect(localStorage.getItem(storageKey)).toBe("exhentai");
    expect(
      screen.getByRole("button", { name: /thirdPartyConfig.navigation.source.label/ }),
    ).toHaveTextContent("ExHentai");
  });

  it("changes the shared selection using the compact selector", async () => {
    const user = userEvent.setup();

    renderPage();
    await user.click(
      screen.getByRole("button", { name: /thirdPartyConfig.navigation.source.label/ }),
    );
    await user.click(await screen.findByRole("option", { name: "TMDB" }));

    expect(screen.getByRole("textbox", { name: "Tmdb setting" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "TMDB" })).toHaveAttribute("aria-selected", "true");
    expect(localStorage.getItem(storageKey)).toBe("tmdb");
  });

  it("keeps a single mounted form while the viewport changes", async () => {
    renderPage();
    const field = screen.getByRole("textbox", { name: "Bilibili setting" });

    fireEvent.change(field, { target: { value: "editing" } });
    act(() => {
      Object.defineProperty(window, "innerWidth", { configurable: true, value: 390 });
      window.dispatchEvent(new Event("resize"));
    });

    expect(screen.getByRole("textbox", { name: "Bilibili setting" })).toBe(field);
    expect(field).toHaveValue("editing");
    expect(within(screen.getByRole("tabpanel")).getAllByRole("textbox")).toHaveLength(1);
  });

  it("still switches sources when browser storage is unavailable", async () => {
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new Error("blocked");
    });
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new Error("blocked");
    });
    const user = userEvent.setup();

    renderPage();

    await user.click(screen.getByRole("tab", { name: "Steam" }));
    await waitFor(() =>
      expect(screen.getByRole("textbox", { name: "Steam setting" })).toBeInTheDocument(),
    );
  });
});
