import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { HeroUIProvider } from "@heroui/react";
import { createInstance } from "i18next";
import { afterEach, beforeAll, beforeEach, expect, it, vi } from "vitest";
import AcquisitionDownloadSources from "../AcquisitionDownloadSources";
import PlanTaskRow from "../PlanTask/PlanTaskRow";
import AcquisitionRow from "@/pages/acquisition/components/AcquisitionRow";
import type { PlanTask } from "../PlanTask/models";
import type { RuntimeOverview, RuntimeSource } from "../PlanTask/runtimeOverview";
import { downloadSourceKey } from "@/components/DownloadSource";
import { ThirdPartyId } from "@/sdk/constants";
import en from "@/locales/en/pages/acquisition.json";
import enums from "@/locales/en/enums.json";

const { copy } = vi.hoisted(() => ({ copy: vi.fn() }));
const i18n = createInstance();
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: i18n.t }),
  initReactI18next: { type: "3rdParty", init: vi.fn() } }));
vi.mock("@/components/bakaui", async () => ({
  Button: (await import("@/components/bakaui/components/Button")).Button,
  Popover: (await import("@/components/bakaui/components/Popover")).default,
  Chip: (await import("@heroui/react")).Chip,
  Progress: () => null,
  Tooltip: ({ children }: any) => children,
  toast: { success: vi.fn() },
}));
vi.mock("@/components/ExternalLink", () => ({
  default: ({ href, children }: any) => <a href={href}>{children}</a>,
}));
vi.mock("@/core/clipboard", () => ({ copyTextToClipboard: copy }));
vi.mock("../AcquisitionTiming", () => ({ default: () => null }));
vi.mock("../AcquisitionCreationOrigin", () => ({ default: () => null }));
vi.mock("@/components/Workflow/Activities", () => ({ getWorkflowActivityUI: () => undefined }));

beforeAll(async () => {
  await i18n.init({ lng: "en", fallbackLng: false, keySeparator: false,
    resources: { en: { translation: { ...en, ...enums,
      "downloadCenter.provider.http": "Direct download",
      "downloadCenter.provider.bt": "BitTorrent",
      "common.state.copied": "Copied",
    } } } });
});
beforeEach(() => { copy.mockReset(); copy.mockResolvedValue(undefined); });
afterEach(cleanup);

const source = (patch: Partial<RuntimeSource> = {}): RuntimeSource => ({
  partId: "main", sourceId: "primary", providerId: "pixeldrain", url: "https://pixeldrain.com/u/first",
  selected: true, status: "available", evidenceKind: "probe", observedAt: "2026-11-18T01:00:00Z",
  isHistorical: false, ...patch,
});
const overview = (sources: RuntimeSource[], patch: Partial<RuntimeOverview> = {}) => ({
  taskId: 940, sources, sourcesTruncated: false, ...patch,
}) as RuntimeOverview;
const show = (sources: RuntimeSource[], patch: Partial<RuntimeOverview> = {}) => render(
  <HeroUIProvider disableAnimation>
    <AcquisitionDownloadSources task={{ id: 940 }} overview={overview(sources, patch)} />
  </HeroUIProvider>,
);

it("groups icons by provider and opens recorded source links without exposing access codes or URL credentials as text", async () => {
  const user = userEvent.setup();
  const first = source({ accessCode: "private-access-code", url: "https://pixeldrain.com/u/first?token=private-token#secret-fragment" });
  show([first, source({ sourceId: "alternative", selected: false, status: "confirmedUnavailable", url: "https://pixeldrain.com/u/second" }),
    source({ sourceId: "baidu", providerId: "baidu", url: "https://pan.baidu.com/s/other" })]);
  const buttons = within(screen.getByRole("group", { name: "Download sources" })).getAllByRole("button");
  expect(buttons).toHaveLength(2);
  expect(buttons[0].querySelector("img")).not.toBeNull();
  expect(screen.queryByRole("link")).toBeNull();
  await user.click(screen.getByRole("button", { name: "View Pixeldrain sources (2)" }));
  const popup = await screen.findByRole("region", { name: "Pixeldrain" });
  expect(within(popup).getByRole("link", { name: "pixeldrain.com/u/first" })).toHaveAttribute("href", first.url);
  expect(within(popup).getByRole("link", { name: "pixeldrain.com/u/second" })).toHaveAttribute("href", "https://pixeldrain.com/u/second");
  expect(within(popup).getByText(en["acquisition.runtime.selectedSource"])).toBeVisible();
  expect(within(popup).getByText(en["acquisition.sourceDetails.alternativeSource"])).toBeVisible();
  expect(within(popup).getByText(en["sourceAvailability.status.confirmedUnavailable"])).toBeVisible();
  expect(popup.textContent).not.toMatch(/private-access-code|private-token|secret-fragment|pan.baidu.com/);
});

it("supports keyboard opening, copying non-web source URLs, and Escape dismissal", async () => {
  const user = userEvent.setup();
  const url = "magnet:?xt=urn:btih:1234567890abcdef";
  show([source({ providerId: "magnet", url })]);
  const trigger = screen.getByRole("button", { name: "View BitTorrent sources (1)" });
  await user.tab();
  expect(trigger).toHaveFocus();
  await user.keyboard("{Enter}");
  const popup = await screen.findByRole("region", { name: "BitTorrent" });
  expect(within(popup).queryByRole("link")).toBeNull();
  await user.click(within(popup).getByRole("button", { name: en["acquisition.sourceDetails.copy.url"] }));
  expect(copy).toHaveBeenCalledWith(url);
  expect(await within(popup).findByText("Copied")).toBeVisible();
  await user.keyboard("{Escape}");
  await waitFor(() => expect(screen.queryByRole("region", { name: "BitTorrent" })).toBeNull());
});

it("keeps expired failure evidence historical and reports truncated source snapshots", async () => {
  const user = userEvent.setup();
  show([source({ status: "confirmedUnavailable", expiresAt: "2020-01-01T00:00:00Z" })], { sourcesTruncated: true });
  await user.click(screen.getByRole("button", { name: "View Pixeldrain sources (1)" }));
  expect(await screen.findByText(`Historical observation: ${en["sourceAvailability.status.confirmedUnavailable"]}`)).toBeVisible();
  expect(screen.getByText(en["acquisition.runtime.historicalHint"])).toBeVisible();
  expect(screen.getByText(en["acquisition.runtime.sourcesTruncated"])).toBeVisible();
});

it("does not classify an unrecognized website as a supported direct download", async () => {
  const user = userEvent.setup();
  show([source({ providerId: "unknown", url: "https://unsupported.example/share/file", status: "unsupported" })]);
  expect(screen.queryByRole("button", { name: /Direct download/ })).toBeNull();
  await user.click(screen.getByRole("button", { name: "View unsupported.example sources (1)" }));
  expect(await screen.findByRole("link", { name: "unsupported.example/share/file" })).toBeVisible();
  expect(screen.getByText(en["sourceAvailability.status.unsupported"])).toBeVisible();
});

it("rejects snapshots from a previous task or run while accepting a saved task without a run", () => {
  const view = render(<AcquisitionDownloadSources task={{ id: 940, workflowRunId: 2 }} overview={overview([source()], { workflowRunId: 1 })} />);
  expect(screen.queryByRole("group")).toBeNull();
  view.rerender(<AcquisitionDownloadSources task={{ id: 941 }} overview={overview([source()])} />);
  expect(screen.queryByRole("group")).toBeNull();
  view.rerender(<HeroUIProvider disableAnimation><AcquisitionDownloadSources task={{ id: 940 }} overview={overview([source()])} /></HeroUIProvider>);
  expect(screen.getByRole("group", { name: "Download sources" })).toBeVisible();
  view.rerender(<AcquisitionDownloadSources task={{ id: 940 }} overview={overview([])} />);
  expect(screen.queryByRole("group")).toBeNull();
});

it.each(["plan", "legacy"])("mounts source icons for no-run %s task rows", variant => {
  const task = { id: 940, title: "Task", resourceName: "Task", status: 1, phase: "preparing" } as PlanTask;
  render(<HeroUIProvider disableAnimation>{variant === "plan"
    ? <PlanTaskRow task={task} overview={overview([source()])} onOpen={vi.fn()} />
    : <AcquisitionRow task={task} overview={overview([source()])} recipes={[]} onChanged={vi.fn()} onEditRecipe={vi.fn()} />
  }</HeroUIProvider>);
  expect(screen.getByRole("button", { name: "View Pixeldrain sources (1)" })).toBeVisible();
});

it.each([["http", "http"], ["magnet", "bt"], ["torrent", "bt"], ["baidu", `site:${ThirdPartyId.BaiduNetdisk}`],
  ["exHentai", `site:${ThirdPartyId.ExHentai}`], ["unknown", undefined]])("uses Download Center's category for provider %s", (provider, expected) => {
  expect(downloadSourceKey(provider!)).toBe(expected);
});
