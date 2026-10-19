import type { ComponentProps } from "react";
import { useState } from "react";
import { HeroUIProvider } from "@heroui/react";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import DownloadCenterFilters from "../DownloadCenterFilters";
import type { CenterFilter, CenterTask } from "../../downloadCenter";
import { indexCenterTasks } from "../../downloadCenter";
import ActualSelect from "@/components/bakaui/components/Select";
import { ThirdPartyId } from "@/sdk/constants";
vi.mock("@/i18n", () => ({ getEnumKey: (name: string, value: string) => `${name}.${value}` }));
vi.mock("@/components/bakaui", async () => ({
  ...(await import("@heroui/react")),
  Button: (await import("@/components/bakaui/components/Button")).Button,
  Select: (props: ComponentProps<typeof ActualSelect>) => <ActualSelect {...props} />,
}));
const task = (id: string, overrides: Partial<CenterTask> = {}): CenterTask => ({
  id,
  providerTaskId: id,
  provider: id.split(":")[0],
  category: id.startsWith("site:") ? "site" : "general",
  name: id,
  outputDirectory: "/downloads",
  status: "running",
  createdAt: "2026-10-19",
  updatedAt: "2026-10-19",
  originKind: "manual",
  files: [],
  availableActions: [],
  ...overrides,
});
const tasks = [
  task("site:1", { thirdPartyId: ThirdPartyId.ExHentai }),
  task("site:2", { thirdPartyId: ThirdPartyId.Bilibili }),
  task("bt:child", { parentId: "site:1" }),
  task("bt:manual", { status: "completed" }),
  task("http:file", { status: "stopped" }),
];
beforeEach(() =>
  vi.stubGlobal(
    "ResizeObserver",
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  ),
);
function mount(initial: CenterFilter = { category: "all" }) {
  const changed = vi.fn();
  function Controlled() {
    const [value, setValue] = useState(initial);
    return (
      <DownloadCenterFilters
        value={value}
        onChange={(next) => {
          changed(next);
          setValue(next);
        }}
        tasks={tasks}
        roots={indexCenterTasks(tasks).roots}
      />
    );
  }
  const view = render(
    <HeroUIProvider disableAnimation>
      <Controlled />
    </HeroUIProvider>,
  );
  return { changed, ...view };
}
async function press(label: string) {
  await act(async () => screen.getByRole("button", { name: new RegExp(label) }).click());
}
const statusButton = (status: string) =>
  screen.getByRole("button", { name: new RegExp(`downloadCenter.status.${status}`) });
describe("download center filters with production controls", () => {
  it("updates and clears the keyword without dropping category, source, or status", async () => {
    const initial: CenterFilter = {
      category: "site",
      provider: `site:${ThirdPartyId.ExHentai}`,
      status: "running",
    };
    const { changed } = mount(initial);
    fireEvent.change(screen.getByLabelText("downloader.filter.keyword"), {
      target: { value: "gallery" },
    });
    expect(changed).toHaveBeenLastCalledWith({ ...initial, keyword: "gallery" });
    await press("clear input");
    expect(changed).toHaveBeenLastCalledWith({ ...initial, keyword: undefined });
    expect(statusButton("running")).toHaveAttribute("aria-pressed", "true");
  });
  it("updates provider through the production selection control", async () => {
    const { changed } = mount({ category: "all", keyword: "gallery" });
    const trigger = document.querySelector<HTMLButtonElement>('button[aria-haspopup="listbox"]')!;
    await act(async () => trigger.click());
    await act(async () =>
      screen.getByRole("option", { name: "downloadCenter.provider.bt" }).click(),
    );
    expect(changed).toHaveBeenLastCalledWith({
      category: "all",
      keyword: "gallery",
      provider: "bt",
      id: undefined,
    });
    expect(trigger).toHaveTextContent("downloadCenter.provider.bt");
    expect(screen.getByRole("button", { name: /downloader.filter.allStatuses/ })).toHaveTextContent(
      "2",
    );
  });
  it("changes category without retaining an incompatible provider or task link", async () => {
    const { changed } = mount({
      category: "general",
      provider: "bt",
      id: "bt:child",
      keyword: "archive",
    });
    await press("downloadCenter.category.site");
    expect(changed).toHaveBeenLastCalledWith({
      category: "site",
      provider: undefined,
      id: undefined,
      keyword: "archive",
    });
  });
  it("switches status and resets filters while retaining the selected category", async () => {
    const { changed } = mount({ category: "general", keyword: "archive" });
    await act(async () => statusButton("running").click());
    expect(changed).toHaveBeenLastCalledWith({
      category: "general",
      keyword: "archive",
      status: "running",
    });
    await press("downloader.filter.allStatuses");
    expect(changed).toHaveBeenLastCalledWith({
      category: "general",
      keyword: "archive",
      status: undefined,
    });
    await press("downloader.filter.reset");
    expect(changed).toHaveBeenLastCalledWith({ category: "general" });
    expect(screen.getByLabelText("downloader.filter.keyword")).toHaveValue("");
  });
});
