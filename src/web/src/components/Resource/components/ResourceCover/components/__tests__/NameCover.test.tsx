import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup } from "@testing-library/react";

import FallbackCover from "../FallbackCover";
import { nameCoverAppearance } from "../NameCover";

const state = vi.hoisted(() => ({ useNameAsCover: undefined as boolean | undefined }));
vi.mock("@/stores/options", () => ({
  useUiOptionsStore: (selector: (value: unknown) => unknown) =>
    selector({ data: { resource: { useNameAsCover: state.useNameAsCover } } }),
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider.tsx", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("@/components/bakaui", () => ({
  Tooltip: ({ children }: { children: React.ReactNode }) => children,
  Button: () => null,
  Modal: () => null,
}));
vi.mock("@/sdk/BApi.tsx", () => ({ default: {} }));

afterEach(() => {
  cleanup();
  state.useNameAsCover = undefined;
});

describe("optional resource name covers", () => {
  it("keeps the existing missing-cover presentation for old settings and when disabled", () => {
    const { rerender } = render(<FallbackCover id={7} name="Blade Runner" />);
    expect(screen.queryByRole("img")).toBeNull();
    state.useNameAsCover = true;
    rerender(<FallbackCover id={7} name="Blade Runner" />);
    expect(screen.getByRole("img", { name: "Blade Runner" })).toBeVisible();
    state.useNameAsCover = false;
    rerender(<FallbackCover id={7} name="Blade Runner" />);
    expect(screen.queryByRole("img")).toBeNull();
  });

  it("keeps a full accessible name for long multilingual titles", () => {
    state.useNameAsCover = true;
    const name = "銀河鉄道の夜 · A journey through the stars · ".repeat(8);
    render(<FallbackCover id={7} name={name} />);
    expect(screen.getByRole("img", { name: name.trim() })).toHaveAttribute("title", name.trim());
    expect(screen.getByText(name.trim())).toBeVisible();
  });

  it("uses consistent artwork for the same normalized name", () => {
    expect(nameCoverAppearance("Ｂｌａｄｅ Runner")).toEqual(nameCoverAppearance("Blade Runner"));
    expect(nameCoverAppearance("夜空の旅").initials).toBe("夜空");
  });
});
