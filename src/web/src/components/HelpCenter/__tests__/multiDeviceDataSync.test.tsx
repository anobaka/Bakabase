import type { ReactElement, ReactNode } from "react";
import type { DeviceId } from "../topics/multiDevice/devices";

import { Children, isValidElement } from "react";
import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import MultiDeviceTopic, { multiDeviceSections } from "../topics/multiDevice";
import MultiDeviceConceptDetail from "../topics/multiDevice/ConceptDetail";
import { multiDeviceConcepts } from "../topics/multiDevice/concepts";
import DataSyncSection, { syncModes } from "../topics/multiDevice/dataSync/DataSyncSection";
import { howStops } from "../topics/multiDevice/dataSync/HowItWorksDiagram";
import { neverSynced, syncedKinds } from "../topics/multiDevice/dataSync/WhatSyncsDiagram";
import { conflictSteps } from "../topics/multiDevice/dataSync/ConflictDiagram";
import { deviceStyle } from "../topics/multiDevice/devices";

import cnCommon from "@/locales/cn/common.json";
import cnDataSyncPage from "@/locales/cn/pages/dataSync.json";
import cnFederation from "@/locales/cn/pages/federation.json";
import cnHelpCenter from "@/locales/cn/components/helpCenter.json";
import cnHelpDataSync from "@/locales/cn/components/helpDataSync.json";
import cnHelpMultiDevice from "@/locales/cn/components/helpMultiDevice.json";
import enCommon from "@/locales/en/common.json";
import enDataSyncPage from "@/locales/en/pages/dataSync.json";
import enFederation from "@/locales/en/pages/federation.json";
import enHelpCenter from "@/locales/en/components/helpCenter.json";
import enHelpDataSync from "@/locales/en/components/helpDataSync.json";
import enHelpMultiDevice from "@/locales/en/components/helpMultiDevice.json";
import { openLocalView } from "@/features/federation/switching";
import { DataSyncKinds } from "@/sdk/constants";

/** Every key asked for while rendering, with the options it was asked with. */
const used = vi.hoisted(() => new Map<string, Record<string, unknown> | undefined>());
/** Who is looking: this device's window, the desktop app showing a managed device, or a browser. */
const reach = vi.hoisted(() => ({
  initialized: true,
  isLocal: true,
  pureClient: false,
  console: false,
  unrestricted: false,
}));

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    // The key, then any interpolated values, so a test can see both.
    t: (key: string, options?: Record<string, unknown>) => {
      used.set(key, options);

      return options ? [key, ...Object.values(options)].join(" ") : key;
    },
    i18n: { language: "en", changeLanguage: vi.fn() },
  }),
  initReactI18next: { type: "3rdParty", init: vi.fn() },
}));
vi.mock("@/stores/remoteAccess", () => ({
  useRemoteAccessStore: (selector: (state: unknown) => unknown) =>
    selector({ initialized: reach.initialized, isLocal: reach.isLocal }),
  useIsPureClient: () => reach.pureClient,
  useIsConsole: () => reach.console,
  // As the store's own: this device, a paired window, or a server open to everyone.
  useCanAdministerShownServer: () =>
    reach.initialized && (reach.isLocal || reach.pureClient || reach.unrestricted),
}));
vi.mock("@/features/federation/switching", () => ({
  DEVICES_ROUTE: "/federation/devices",
  openLocalView: vi.fn(),
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress }: { children: ReactNode; onPress?: () => void }) => (
    <button type="button" onClick={onPress}>
      {children}
    </button>
  ),
  Tab: () => null,
  Tabs: ({
    children,
    selectedKey,
    onSelectionChange,
    "aria-label": label,
  }: {
    children: ReactNode;
    selectedKey: string;
    onSelectionChange: (key: string) => void;
    "aria-label": string;
  }) => (
    <div aria-label={label} role="tablist">
      {Children.toArray(children)
        .filter(isValidElement)
        .map((child) => {
          const key = String((child as ReactElement).key).replace(/^\.\$/, "");

          return (
            <button
              key={key}
              aria-selected={key === selectedKey}
              role="tab"
              type="button"
              onClick={() => onSelectionChange(key)}
            >
              {(child as ReactElement<{ title: ReactNode }>).props.title}
            </button>
          );
        })}
    </div>
  ),
}));

const d = (key: string) => `helpCenter.dataSync.${key}`;
const m = (key: string) => `helpCenter.multiDevice.${key}`;

const setReach = (next: Partial<typeof reach>) => Object.assign(reach, next);
const ownWindow = {
  initialized: true,
  isLocal: true,
  pureClient: false,
  console: false,
  unrestricted: false,
};
const managedWindow = { ...ownWindow, isLocal: false, pureClient: true, console: true };
const unrestrictedBrowser = { ...ownWindow, isLocal: false, unrestricted: true };
const pairedOnlyBrowser = { ...ownWindow, isLocal: false };

beforeEach(() => {
  setReach(ownWindow);
  vi.mocked(openLocalView).mockReset();
});
afterEach(() => cleanup());

const arrows = (root: ParentNode) =>
  Array.from(root.querySelectorAll("[data-arrow]")).map((arrow) => ({
    from: arrow.getAttribute("data-from"),
    to: arrow.getAttribute("data-to"),
    state: arrow.getAttribute("data-state"),
  }));

/** Every point of an SVG path made of straight segments ("M x y L x y …"). */
const pathPoints = (path: Element) =>
  Array.from(path.getAttribute("d")!.matchAll(/[ML]\s*(-?[\d.e-]+)[\s,]+(-?[\d.e-]+)/g)).map(
    ([, x, y]) => ({ x: Number(x), y: Number(y) }),
  );

/** Where an SVG path starts ("M x y …"). */
const pathStart = (path: Element) => pathPoints(path)[0];

/** Where an arrow points, read from what is drawn: from its line's start to its head's tip. */
const pointing = (arrow: Element) => {
  const [line, head] = Array.from(arrow.querySelectorAll("path"));
  const tail = pathStart(line);
  const tip = pathStart(head);
  const dx = tip.x - tail.x;
  const dy = tip.y - tail.y;

  if (Math.abs(dx) >= Math.abs(dy)) return dx < 0 ? "left" : "right";

  return dy < 0 ? "up" : "down";
};

describe("data sync help: registration", () => {
  it("is the multi-device topic's last tab, and opens there", () => {
    expect(multiDeviceSections.at(-1)).toBe("dataSync");

    render(<MultiDeviceTopic section="dataSync" onNavigate={vi.fn()} />);
    expect(screen.getByRole("tab", { name: m("section.dataSync") })).toHaveAttribute(
      "aria-selected",
      "true",
    );
    expect(screen.getByTestId("data-sync-help")).toBeInTheDocument();
  });

  it("opens from its tab like every other section", () => {
    render(<MultiDeviceTopic onNavigate={vi.fn()} />);
    expect(screen.queryByTestId("data-sync-help")).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("tab", { name: m("section.dataSync") }));
    expect(screen.getByTestId("data-sync-help")).toBeInTheDocument();
  });

  it("adds the concept, rendered by the topic's own concept page", () => {
    expect(multiDeviceConcepts.at(-1)).toEqual({
      id: "dataSync",
      labelKey: m("concept.dataSync.name"),
    });

    render(<MultiDeviceConceptDetail conceptId="dataSync" />);
    expect(screen.getByRole("heading", { name: m("concept.dataSync.name") })).toBeInTheDocument();
    expect(screen.getByText(m("concept.dataSync.short"))).toBeInTheDocument();
    expect(screen.getByText(m("concept.dataSync.long"))).toBeInTheDocument();
    expect(screen.getByText(m("concept.dataSync.example"))).toBeInTheDocument();
  });
});

describe("data sync help: diagrams", () => {
  it("draws the three diagrams and the ways to sync", () => {
    render(<DataSyncSection onNavigate={vi.fn()} />);

    for (const id of ["data-sync-how", "data-sync-what", "data-sync-conflict", "data-sync-modes"]) {
      expect(screen.getByTestId(id)).toBeInTheDocument();
    }
    expect(screen.getByRole("figure", { name: d("how.title") })).toBeInTheDocument();
    expect(screen.getByRole("figure", { name: d("types.title") })).toBeInTheDocument();
    expect(screen.getByRole("figure", { name: d("conflicts.title") })).toBeInTheDocument();
  });

  it("walks a link through its five stops, in order", () => {
    render(<DataSyncSection onNavigate={vi.fn()} />);
    const stops = screen.getByTestId("data-sync-how").querySelectorAll("li[data-stop]");

    expect(Array.from(stops).map((stop) => stop.getAttribute("data-stop"))).toEqual([
      "link",
      "review",
      "inStep",
      "needsYou",
      "undo",
    ]);
    stops.forEach((stop, index) => {
      const id = howStops[index].id;

      expect(stop).toHaveTextContent(String(index + 1));
      expect(stop).toHaveTextContent(d(`how.${id}.title`));
    });
  });

  it("points every arrow at the device that receives, in the sender's colour", () => {
    render(<DataSyncSection onNavigate={vi.fn()} />);
    const stop = (id: string) =>
      screen.getByTestId("data-sync-how").querySelector(`[data-stop="${id}"]`)!;
    const toHere = { from: "laptop", to: "desktop" };
    const toThere = { from: "desktop", to: "laptop" };

    // This device asks to receive the laptop's definitions; the laptop has yet to approve.
    expect(arrows(stop("link"))).toEqual([{ ...toHere, state: "pending" }]);
    expect(arrows(stop("review"))).toEqual([{ ...toHere, state: "active" }]);
    expect(arrows(stop("inStep"))).toEqual([
      { ...toHere, state: "active" },
      { ...toThere, state: "active" },
    ]);
    expect(stop("needsYou").querySelectorAll('[data-badge="attention"]')).toHaveLength(2);

    const mode = (id: string) =>
      screen.getByTestId("data-sync-modes").querySelector(`[data-mode="${id}"]`)!;

    expect(arrows(mode("follow"))).toEqual([{ ...toHere, state: "active" }]);
    expect(arrows(mode("twoWay"))).toEqual([
      { ...toHere, state: "active" },
      { ...toThere, state: "active" },
    ]);
    // Copied once, then off: not the live lane of receiving only, and its sign is text that
    // keeps a readable size, not part of the scaled drawing.
    expect(arrows(mode("copyOnce"))).toEqual([{ ...toHere, state: "once" }]);
    const mark = mode("copyOnce").querySelector("[data-mark]")!;

    expect(mark).toHaveTextContent("1×");
    expect(mark.closest("svg")).toBeNull();
    expect(mark).toHaveClass("text-xs");
    expect(mode("follow").querySelector("[data-mark]")).toBeNull();

    // The decision travels from the laptop, where it was made, to this device.
    const decide = screen.getByTestId("data-sync-conflict").querySelector('[data-step="decide"]')!;

    expect(arrows(decide).length).toBeGreaterThan(0);
    expect(arrows(decide).every((arrow) => arrow.from === "laptop" && arrow.to === "desktop")).toBe(
      true,
    );
  });

  it("draws each arrow in the sender's colour, with its head at the receiver", () => {
    render(<DataSyncSection onNavigate={vi.fn()} />);
    const help = screen.getByTestId("data-sync-help");

    // Every picture puts this device first: on the left, or on top where the steps stack.
    const pictures = help.querySelectorAll("[data-stop], [data-mode], [data-step]");

    expect(pictures.length).toBeGreaterThan(0);
    pictures.forEach((picture) => {
      const devices = Array.from(picture.querySelectorAll("[data-device]")).map((device) =>
        device.getAttribute("data-device"),
      );

      expect(devices).toEqual(["desktop", "laptop"]);
    });

    const drawn = Array.from(help.querySelectorAll("[data-arrow]"));

    expect(drawn.length).toBeGreaterThan(0);
    for (const arrow of drawn) {
      const from = arrow.getAttribute("data-from") as DeviceId;
      const to = arrow.getAttribute("data-to") as DeviceId;
      const [line, head] = Array.from(arrow.querySelectorAll("path"));
      const idle = arrow.getAttribute("data-state") === "idle";

      expect(line).toHaveClass(idle ? "stroke-default-300" : deviceStyle(from).stroke);
      expect(head).toHaveClass(idle ? "fill-default-300" : deviceStyle(from).solid);
      expect(to === "desktop" ? ["left", "up"] : ["right", "down"]).toContain(pointing(arrow));

      // Only a copy made once stops at a bar across its tail, in the sender's colour.
      const bar = arrow.querySelector("[data-bar]");

      if (arrow.getAttribute("data-state") !== "once") {
        expect(bar).toBeNull();
        continue;
      }
      expect(line).not.toHaveAttribute("stroke-dasharray");
      expect(bar).toHaveClass(deviceStyle(from).stroke);
      const [end1, end2] = pathPoints(bar!);
      const tail = pathStart(line);
      const tip = pathStart(head);

      expect((end1.x + end2.x) / 2).toBeCloseTo(tail.x);
      expect((end1.y + end2.y) / 2).toBeCloseTo(tail.y);
      expect(Math.hypot(end2.x - end1.x, end2.y - end1.y)).toBeGreaterThan(4);
      // Across the line, not along it.
      expect(
        (end2.x - end1.x) * (tip.x - tail.x) + (end2.y - end1.y) * (tip.y - tail.y),
      ).toBeCloseTo(0);
    }
    // Both colours and both ways are drawn, so neither check above passes vacuously.
    expect(new Set(drawn.map((arrow) => arrow.getAttribute("data-from")))).toEqual(
      new Set(["desktop", "laptop"]),
    );
    expect(new Set(drawn.map(pointing))).toEqual(new Set(["left", "right", "up"]));
    // Every look a lane can have is drawn somewhere, so the bar checks are not vacuous either.
    expect(new Set(drawn.map((arrow) => arrow.getAttribute("data-state")))).toEqual(
      new Set(["active", "once", "pending", "idle"]),
    );
  });

  it("names every mode with the terminology's words", () => {
    render(<DataSyncSection onNavigate={vi.fn()} />);
    const modes = screen.getByTestId("data-sync-modes");

    expect(syncModes.map((mode) => mode.id)).toEqual(["follow", "twoWay", "copyOnce"]);
    for (const mode of syncModes) {
      expect(within(modes).getByText(d(`modes.${mode.id}.title`))).toBeInTheDocument();
      expect(within(modes).getByText(d(`modes.${mode.id}.desc`))).toBeInTheDocument();
    }
    expect(within(modes).getByText(d("modes.loops"))).toBeInTheDocument();
    expect(enHelpDataSync[d("modes.follow.title") as keyof typeof enHelpDataSync]).toBe(
      "Receive only",
    );
    expect(cnHelpDataSync[d("modes.follow.title") as keyof typeof cnHelpDataSync]).toBe("仅接收");
    expect(enHelpDataSync[d("modes.twoWay.title") as keyof typeof enHelpDataSync]).toBe(
      "Keep in step both ways",
    );
    expect(cnHelpDataSync[d("modes.twoWay.title") as keyof typeof cnHelpDataSync]).toBe(
      "双向保持一致",
    );
    expect(enHelpDataSync[d("modes.copyOnce.title") as keyof typeof enHelpDataSync]).toBe(
      "Copy once",
    );
    expect(cnHelpDataSync[d("modes.copyOnce.title") as keyof typeof cnHelpDataSync]).toBe(
      "只复制一次",
    );
  });

  it("lists every kind that syncs and everything that never does", () => {
    // Adding a kind on the server fails here until the help says what it carries.
    expect([...syncedKinds].sort()).toEqual([...DataSyncKinds].sort());

    render(<DataSyncSection onNavigate={vi.fn()} />);
    const what = screen.getByTestId("data-sync-what");

    for (const kind of syncedKinds) {
      const item = what.querySelector(`[data-kind="${kind}"]`)!;

      expect(item).toHaveTextContent(d(`types.${kind}.title`));
      expect(item).toHaveTextContent(d(`types.${kind}.desc`));
    }
    expect(within(what).getByText(d("types.more"))).toBeInTheDocument();

    const never = screen.getByTestId("data-sync-never");

    expect(
      Array.from(never.querySelectorAll("[data-never]")).map((item) => item.textContent),
    ).toEqual(neverSynced.map((item) => d(`never.${item.id}`)));
    expect(neverSynced.map((item) => item.id)).toEqual([
      "library",
      "paths",
      "programs",
      "secrets",
      "tasks",
      "settings",
    ]);
  });

  it("shows a rename conflict asked on both devices and closed by one decision", () => {
    render(<DataSyncSection onNavigate={vi.fn()} />);
    const conflict = screen.getByTestId("data-sync-conflict");
    const step = (id: string) => conflict.querySelector(`[data-step="${id}"]`)!;
    const panel = (id: string, device: string) =>
      step(id).querySelector(`[data-device="${device}"]`)!;

    expect(
      Array.from(conflict.querySelectorAll("[data-step]")).map((item) =>
        item.getAttribute("data-step"),
      ),
    ).toEqual([...conflictSteps]);

    expect(panel("rename", "desktop")).toHaveTextContent(d("conflicts.diagram.sample.here"));
    expect(panel("rename", "laptop")).toHaveTextContent(d("conflicts.diagram.sample.there"));
    // A screen reader hears which name is the old one and which the new, and the arrow too.
    for (const [device, renamed] of [
      ["desktop", "here"],
      ["laptop", "there"],
    ]) {
      const rename = panel("rename", device);

      expect(rename.querySelector("del")).toHaveTextContent(d("conflicts.diagram.sample.base"));
      expect(rename.querySelector("ins")).toHaveTextContent(
        d(`conflicts.diagram.sample.${renamed}`),
      );
      const arrow = Array.from(rename.querySelectorAll("span")).find(
        (span) => span.textContent === "→",
      );

      expect(arrow).toBeDefined();
      expect(arrow!.closest("[aria-hidden]")).toBeNull();
    }
    for (const device of ["desktop", "laptop"]) {
      expect(panel("ask", device)).toHaveTextContent(d("conflicts.diagram.card"));
    }
    expect(panel("decide", "laptop")).toHaveTextContent(d("conflicts.diagram.decidedHere"));
    expect(panel("decide", "desktop").querySelector("[data-closed]")).toHaveTextContent(
      `${d("conflicts.diagram.resolvedOn")} ${m("device.laptop")}`,
    );

    const lines = screen.getByTestId("data-sync-conflicts");

    for (const id of ["fields", "same", "hub", "types", "case", "labelWriters"]) {
      expect(within(lines).getByText(d(`conflicts.${id}`))).toBeInTheDocument();
    }
  });

  it("covers deletions, undo, pauses, restores and what sync never does", () => {
    render(<DataSyncSection onNavigate={vi.fn()} />);

    const deletes = screen.getByTestId("data-sync-deletes");

    for (const id of ["options", "definitions", "edited", "filters"]) {
      expect(within(deletes).getByText(d(`deletes.${id}`))).toBeInTheDocument();
    }

    const recover = screen.getByTestId("data-sync-recover");

    for (const id of ["undo", "pause", "restore"]) {
      expect(recover.querySelector(`[data-recover="${id}"]`)).toHaveTextContent(d(`${id}.desc`));
    }

    const safety = screen.getByTestId("data-sync-safety");

    for (const id of ["noValues", "noMerge", "noRemote", "stop", "grant"]) {
      expect(within(safety).getByText(d(`safety.${id}`))).toBeInTheDocument();
    }
  });
});

describe("data sync help: where it can open", () => {
  const where = () => screen.getByTestId("data-sync-where");
  const openButton = () => screen.queryByRole("button", { name: d("open") });

  it("names the Device map and opens both pages in this device's own window", () => {
    const onNavigate = vi.fn();

    render(<DataSyncSection onNavigate={onNavigate} />);
    expect(screen.getByText(d("how.link.desc"))).toBeInTheDocument();
    expect(screen.queryByText(d("how.link.descNoMap"))).not.toBeInTheDocument();
    expect(within(where()).getByText(d("where.map"))).toBeInTheDocument();

    fireEvent.click(within(where()).getByRole("button", { name: m("open.map") }));
    expect(onNavigate).toHaveBeenLastCalledWith("/federation/map");

    fireEvent.click(openButton()!);
    expect(onNavigate).toHaveBeenLastCalledWith("/data-sync");
    expect(openLocalView).not.toHaveBeenCalled();
  });

  it("opens the managed device's page, and switches back for this computer's map", async () => {
    setReach(managedWindow);
    // A switch that succeeds replaces the page, so it never settles here.
    vi.mocked(openLocalView).mockReturnValueOnce(new Promise<never>(() => {}));
    const onNavigate = vi.fn();

    render(<DataSyncSection onNavigate={onNavigate} />);
    expect(screen.getByText(d("how.link.desc"))).toBeInTheDocument();

    // Data sync is not a page of this device only: it opens on the device the window shows.
    fireEvent.click(openButton()!);
    expect(onNavigate).toHaveBeenLastCalledWith("/data-sync");

    await act(async () => {
      fireEvent.click(within(where()).getByRole("button", { name: m("open.map") }));
    });
    expect(openLocalView).toHaveBeenCalledWith("/federation/map");
  });

  it("drops the map for a browser, and offers the page only where it may be administered", () => {
    setReach(unrestrictedBrowser);
    const onNavigate = vi.fn();

    render(<DataSyncSection onNavigate={onNavigate} />);
    expect(screen.getByText(d("how.link.descNoMap"))).toBeInTheDocument();
    expect(screen.queryByText(d("how.link.desc"))).not.toBeInTheDocument();
    expect(screen.queryByText(d("where.map"))).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: m("open.map") })).not.toBeInTheDocument();
    expect(within(where()).getByText(d("where.page"))).toBeInTheDocument();
    fireEvent.click(openButton()!);
    expect(onNavigate).toHaveBeenLastCalledWith("/data-sync");
    cleanup();

    setReach(pairedOnlyBrowser);
    render(<DataSyncSection onNavigate={onNavigate} />);
    expect(screen.getByText(d("how.link.descNoMap"))).toBeInTheDocument();
    expect(openButton()).not.toBeInTheDocument();
  });

  it("offers no button where the help cannot navigate", () => {
    render(<DataSyncSection />);
    expect(openButton()).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: m("open.map") })).not.toBeInTheDocument();
  });
});

describe("data sync help: translations", () => {
  const placeholders = (text: string) => (text.match(/{{\s*\w+\s*}}/g) ?? []).sort();
  const en = {
    ...enHelpCenter,
    ...enFederation,
    ...enHelpMultiDevice,
    ...enHelpDataSync,
  } as Record<string, string>;
  const cn = {
    ...cnHelpCenter,
    ...cnFederation,
    ...cnHelpMultiDevice,
    ...cnHelpDataSync,
  } as Record<string, string>;
  const enOwn = enHelpDataSync as Record<string, string>;
  const cnOwn = cnHelpDataSync as Record<string, string>;

  /** Renders the section in each variant, and the concept, recording every key. */
  const renderEverything = () => {
    used.clear();
    for (const who of [ownWindow, managedWindow, unrestrictedBrowser, pairedOnlyBrowser]) {
      setReach(who);
      render(<MultiDeviceTopic section="dataSync" onNavigate={vi.fn()} />);
      cleanup();
    }
    setReach(ownWindow);
    render(<MultiDeviceConceptDetail conceptId="dataSync" />);
    cleanup();
    // The concept's name is read by the help center's navigation, not by the concept page.
    used.set(m("concept.dataSync.name"), undefined);
  };

  it("uses only keys that exist in both English and Chinese, with their placeholders", () => {
    renderEverything();

    expect([...used.keys()].filter((key) => !(key in en))).toEqual([]);
    expect([...used.keys()].filter((key) => !(key in cn))).toEqual([]);
    for (const [key, options] of used) {
      for (const name of Object.keys(options ?? {})) {
        expect(en[key], key).toContain(`{{${name}}}`);
        expect(cn[key], key).toContain(`{{${name}}}`);
      }
    }
  });

  it("ships no key the section never shows", () => {
    renderEverything();

    expect(Object.keys(enOwn).filter((key) => !used.has(key))).toEqual([]);
  });

  it("keeps the two languages in step", () => {
    expect(Object.keys(cnOwn).sort()).toEqual(Object.keys(enOwn).sort());
    for (const [key, text] of Object.entries(enOwn)) {
      expect(text.trim(), key).not.toBe("");
      expect(cnOwn[key].trim(), key).not.toBe("");
      expect(placeholders(cnOwn[key]), key).toEqual(placeholders(text));
    }
  });

  it("uses the topic's prefix for the tab and the concept, and its own for the rest", () => {
    for (const key of Object.keys(enOwn)) {
      expect(
        key.startsWith("helpCenter.dataSync.") ||
          key === m("section.dataSync") ||
          key.startsWith(m("concept.dataSync.")),
        key,
      ).toBe(true);
    }
  });

  it("never quotes a device name", () => {
    // An opening mark before a name, or a closing one after it (an apostrophe after a name
    // is English's possessive, not a quote).
    const quoted = /[«“「"']\s*{{\s*\w+\s*}}|{{\s*\w+\s*}}\s*[»”」"]/;

    for (const [key, text] of [...Object.entries(enOwn), ...Object.entries(cnOwn)]) {
      expect(quoted.test(text), key).toBe(false);
    }
  });

  it("speaks the feature's own words", () => {
    // The tab carries the menu's name for the page.
    expect(enOwn[m("section.dataSync")]).toBe(
      (enCommon as Record<string, string>)["menu.dataSync"],
    );
    expect(cnOwn[m("section.dataSync")]).toBe(
      (cnCommon as Record<string, string>)["menu.dataSync"],
    );
    expect(cnOwn[m("section.dataSync")]).toBe("数据同步");
    // "Needs you" is 待你决定; 待处理 already means something else in the app.
    expect(cnOwn[d("how.needsYou.title")]).toBe("待你决定");
    // The inbox's action has a fixed name: 仅保留在本机.
    expect(cnOwn[d("deletes.options")]).toContain("仅保留在本机");

    for (const [key, text] of Object.entries(cnOwn)) {
      for (const word of [
        "配置同步",
        "配置包",
        "分享给他人",
        "待处理",
        "设备地图",
        "只保留在本机",
      ]) {
        expect(text.includes(word), `${key}: ${word}`).toBe(false);
      }
    }
    // Where the help names the Device map, it uses the page's own title.
    const mapTitle = (cnFederation as Record<string, string>)["federation.map.title"];

    expect(cnOwn[d("where.map")]).toContain(mapTitle);
    expect(cnOwn[d("how.link.desc")]).toContain(mapTitle);
    expect(enOwn[d("where.map")]).toContain(
      (enFederation as Record<string, string>)["federation.map.title"],
    );
  });

  it("quotes the Data sync page's own labels, once the page has them", () => {
    // Where the help names a control or a tab of the page, it uses the page's words: `is`
    // is the label itself (a button's closing ellipsis left out), `quotes` names it in a
    // sentence.
    const quoted: { help: string; page: string; how: "is" | "quotes" }[] = [
      { help: d("modes.follow.title"), page: "dataSync.mode.follow", how: "is" },
      { help: d("modes.twoWay.title"), page: "dataSync.mode.twoWay", how: "is" },
      { help: d("modes.copyOnce.title"), page: "dataSync.copyOnce.button", how: "is" },
      { help: d("how.link.desc"), page: "dataSync.thisDevice", how: "quotes" },
      { help: d("how.link.desc"), page: "dataSync.sharing.label", how: "quotes" },
      { help: d("how.link.desc"), page: "dataSync.mode.follow", how: "quotes" },
      { help: d("how.link.desc"), page: "dataSync.mode.twoWay", how: "quotes" },
      { help: d("how.link.descNoMap"), page: "dataSync.thisDevice", how: "quotes" },
      { help: d("how.link.descNoMap"), page: "dataSync.wizard.open", how: "quotes" },
      { help: d("how.needsYou.desc"), page: "dataSync.inbox.title", how: "quotes" },
    ];
    const pages = [
      [enOwn, enDataSyncPage as Record<string, string>],
      [cnOwn, cnDataSyncPage as Record<string, string>],
    ] as const;

    // The page's labels come with the page itself; until then there is nothing to compare.
    // Once they are there, every label the help quotes must be among them.
    for (const [help, page] of pages) {
      if (Object.keys(page).length === 0) continue;
      for (const { help: helpKey, page: pageKey, how } of quoted) {
        const label = page[pageKey];

        expect(label, pageKey).toBeTruthy();
        if (how === "is") expect(help[helpKey], helpKey).toBe(label.replace(/…$/, ""));
        else expect(help[helpKey], `${helpKey} → ${pageKey}`).toContain(label);
      }
    }
  });

  it("amends the topic's storage pillar and map words for data sync", () => {
    const enTopic = enHelpMultiDevice as Record<string, string>;
    const cnTopic = cnHelpMultiDevice as Record<string, string>;

    expect(enTopic[m("pillar.storage.desc")]).toContain("only the definitions you choose to sync");
    expect(cnTopic[m("pillar.storage.desc")]).toContain("只有你选择同步的定义会保持一致");
    expect(enTopic[m("map.desc")]).toMatch(/Data sync lines show which device receives/);
    expect(cnTopic[m("map.desc")]).toContain("数据同步连线显示哪台设备接收另一台的定义");
  });
});
