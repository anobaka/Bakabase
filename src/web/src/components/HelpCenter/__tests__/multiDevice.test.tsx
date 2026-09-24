import type { ReactElement, ReactNode } from "react";

import { Children, isValidElement } from "react";
import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { getHelpTopic, helpTopics } from "../topics";
import MultiDeviceTopic, { multiDeviceSections } from "../topics/multiDevice";
import MultiDeviceConceptDetail from "../topics/multiDevice/ConceptDetail";
import { multiDeviceConcepts } from "../topics/multiDevice/concepts";
import { devices } from "../topics/multiDevice/devices";
import { linkState } from "../topics/multiDevice/NetworkDiagram";

import cnFederation from "@/locales/cn/pages/federation.json";
import cnHelpCenter from "@/locales/cn/components/helpCenter.json";
import cnHelp from "@/locales/cn/components/helpMultiDevice.json";
import enFederation from "@/locales/en/pages/federation.json";
import enHelpCenter from "@/locales/en/components/helpCenter.json";
import enHelp from "@/locales/en/components/helpMultiDevice.json";
import { openLocalView } from "@/features/federation/switching";

/** Every key asked for while rendering, with the options it was asked with. */
const used = vi.hoisted(() => new Map<string, Record<string, unknown> | undefined>());
const reach = vi.hoisted(() => ({
  initialized: true,
  isLocal: true,
  pureClient: false,
  console: false,
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

const k = (key: string) => `helpCenter.multiDevice.${key}`;

const setReach = (next: Partial<typeof reach>) => Object.assign(reach, next);

beforeEach(() => {
  setReach({ initialized: true, isLocal: true, pureClient: false, console: false });
  vi.mocked(openLocalView).mockReset();
});
afterEach(() => cleanup());

const openTab = (id: string) =>
  fireEvent.click(screen.getByRole("tab", { name: k(`section.${id}`) }));

describe("multi-device help topic: registration", () => {
  it("is listed in the help center with its concepts", () => {
    const topic = helpTopics.find((item) => item.id === "multiDevice");

    expect(topic).toBeDefined();
    expect(getHelpTopic("multiDevice")).toBe(topic);
    expect(topic!.titleKey).toBe("helpCenter.topic.multiDevice");
    expect(topic!.Content).toBe(MultiDeviceTopic);
    expect(topic!.ConceptContent).toBe(MultiDeviceConceptDetail);
    expect(topic!.concepts?.map((concept) => concept.id)).toEqual([
      "sharing",
      "management",
      "pathMapping",
      "remoteAccess",
      "thinClient",
    ]);
  });
});

describe("multi-device help topic: sections", () => {
  it("opens on the overview and renders every section from its tab", () => {
    render(<MultiDeviceTopic onNavigate={vi.fn()} />);

    expect(screen.getAllByRole("tab").map((tab) => tab.textContent)).toEqual(
      multiDeviceSections.map((id) => k(`section.${id}`)),
    );
    expect(screen.getByTestId("multi-device-network")).toBeInTheDocument();
    expect(screen.getByText(k("ways.title"))).toBeInTheDocument();
    expect(screen.getAllByRole("row")).toHaveLength(5);

    openTab("browse");
    expect(screen.getByTestId("multi-device-library-mockup")).toBeInTheDocument();
    expect(screen.queryByTestId("multi-device-network")).not.toBeInTheDocument();

    openTab("switch");
    expect(screen.getByTestId("multi-device-switch-mockup")).toBeInTheDocument();
    expect(screen.getByTestId("multi-device-play-here")).toBeInTheDocument();

    openTab("setup");
    expect(document.querySelector('[data-track="browse"]')).not.toBeNull();
    expect(document.querySelector('[data-track="manage"]')).not.toBeNull();
    expect(screen.getByText(k("setup.unrestricted"))).toBeInTheDocument();
    expect(screen.getByText(k("setup.thinClient"))).toBeInTheDocument();
  });

  it("opens at the requested section, and ignores other topics' sections", () => {
    const { unmount } = render(<MultiDeviceTopic section="switch" />);

    expect(screen.getByRole("tab", { name: k("section.switch") })).toHaveAttribute(
      "aria-selected",
      "true",
    );
    unmount();

    render(<MultiDeviceTopic section="triggers" />);
    expect(screen.getByRole("tab", { name: k("section.whatIs") })).toHaveAttribute(
      "aria-selected",
      "true",
    );
  });

  it("jumps from an overview card to its section", () => {
    render(<MultiDeviceTopic />);

    const switchCard = document.querySelector('[data-way="switch"]') as HTMLElement;

    fireEvent.click(within(switchCard).getByRole("button", { name: k("ways.more") }));
    expect(screen.getByTestId("multi-device-switch-mockup")).toBeInTheDocument();
  });

  it("renders every concept, and nothing for an unknown one", () => {
    for (const concept of multiDeviceConcepts) {
      const { unmount } = render(<MultiDeviceConceptDetail conceptId={concept.id} />);

      expect(screen.getByRole("heading", { name: concept.labelKey })).toBeInTheDocument();
      expect(screen.getByText(k(`concept.${concept.id}.long`))).toBeInTheDocument();
      unmount();
    }

    render(<MultiDeviceConceptDetail conceptId="pathMapping" />);
    expect(screen.getByTestId("multi-device-play-here")).toBeInTheDocument();
    cleanup();

    render(<MultiDeviceConceptDetail conceptId="remoteAccess" />);
    expect(within(screen.getByTestId("remote-access-modes")).getAllByRole("row")).toHaveLength(5);
    cleanup();

    const { container } = render(<MultiDeviceConceptDetail conceptId="nope" />);

    expect(container).toBeEmptyDOMElement();
  });
});

describe("multi-device help topic: network diagram", () => {
  const link = (id: string) => document.querySelector(`[data-link="${id}"]`)!;
  const deviceButton = (id: string) =>
    document.querySelector(`[data-device="${id}"][role="button"]`)!;
  const caption = () => screen.getByTestId("multi-device-network").querySelector("p[aria-live]")!;

  it("highlights the links of the selected device, pointing the way access goes", () => {
    render(<MultiDeviceTopic />);

    // The desktop PC is where the reader sits: its links point away from it.
    expect(deviceButton("desktop")).toHaveAttribute("aria-pressed", "true");
    expect(link("desktop-laptop")).toHaveAttribute("data-active", "true");
    expect(link("desktop-laptop")).toHaveAttribute("data-from", "desktop");
    expect(link("desktop-nas")).toHaveAttribute("data-active", "true");
    expect(link("desktop-nas")).toHaveAttribute("data-from", "desktop");
    expect(link("laptop-nas")).toHaveAttribute("data-active", "false");

    // The NAS has no screen: picking it shows the computers reaching in.
    fireEvent.click(deviceButton("nas"));
    expect(deviceButton("nas")).toHaveAttribute("aria-pressed", "true");
    expect(deviceButton("desktop")).toHaveAttribute("aria-pressed", "false");
    expect(link("desktop-laptop")).toHaveAttribute("data-active", "false");
    expect(link("desktop-nas")).toHaveAttribute("data-to", "nas");
    expect(link("laptop-nas")).toHaveAttribute("data-active", "true");
    expect(link("laptop-nas")).toHaveAttribute("data-to", "nas");
    expect(caption()).toHaveTextContent(k("diagram.caption.nas"));

    // Keyboard selection works like a click.
    fireEvent.keyDown(deviceButton("laptop"), { key: "Enter" });
    expect(deviceButton("laptop")).toHaveAttribute("aria-pressed", "true");
    expect(link("laptop-nas")).toHaveAttribute("data-from", "laptop");
    expect(link("desktop-laptop")).toHaveAttribute("data-from", "laptop");
    expect(link("desktop-nas")).toHaveAttribute("data-active", "false");
    expect(caption()).toHaveTextContent(k("diagram.caption.laptop"));
  });

  it("gives every device a named, focusable control", () => {
    render(<MultiDeviceTopic />);

    for (const device of devices) {
      const control = deviceButton(device.id);

      expect(control).toHaveAttribute("tabindex", "0");
      expect(control.getAttribute("aria-label")).toContain(k(`device.${device.id}`));
    }
    expect(screen.getByRole("group", { name: k("diagram.title") })).toBeInTheDocument();
  });

  it("derives link direction from whether a device has a window", () => {
    expect(linkState("desktop", ["desktop", "nas"])).toEqual({
      active: true,
      from: "desktop",
      to: "nas",
    });
    expect(linkState("nas", ["desktop", "nas"])).toEqual({
      active: true,
      from: "desktop",
      to: "nas",
    });
    expect(linkState("laptop", ["desktop", "nas"]).active).toBe(false);
  });
});

describe("multi-device help topic: mock-ups", () => {
  it("merges other devices into the library list when the scope widens", () => {
    render(<MultiDeviceTopic section="browse" />);
    const mockup = screen.getByTestId("multi-device-library-mockup");
    const tiles = () => Array.from(mockup.querySelectorAll("li[data-device]"));

    expect(tiles()).toHaveLength(6);
    expect(new Set(tiles().map((tile) => tile.getAttribute("data-device")))).toEqual(
      new Set(["desktop", "laptop", "nas"]),
    );

    fireEvent.click(within(mockup).getByRole("button", { name: "federation.scope.local" }));
    expect(tiles()).toHaveLength(2);
    expect(tiles().every((tile) => tile.getAttribute("data-device") === "desktop")).toBe(true);
    expect(screen.getByText(k("browse.mock.caption.local"))).toBeInTheDocument();
  });

  it("switches the mock window between this device and a managed one", () => {
    render(<MultiDeviceTopic section="switch" />);
    const mockup = screen.getByTestId("multi-device-switch-mockup");
    const switcher = within(mockup).getByTestId("mock-switcher");

    expect(switcher).toHaveTextContent("federation.switcher.managing");
    expect(mockup.querySelector("[data-shown]")).toHaveAttribute("data-shown", "nas");

    fireEvent.click(mockup.querySelector('button[data-device="desktop"]')!);
    expect(switcher).not.toHaveTextContent("federation.switcher.managing");
    expect(mockup.querySelector("[data-shown]")).toHaveAttribute("data-shown", "desktop");
    expect(screen.getByText(k("switch.mock.caption.local"))).toBeInTheDocument();

    fireEvent.click(mockup.querySelector('button[data-device="laptop"]')!);
    expect(switcher).toHaveTextContent(k("device.laptop"));
    expect(mockup.querySelector('button[data-device="laptop"]')).toHaveAttribute(
      "aria-pressed",
      "true",
    );
  });
});

describe("multi-device help topic: links to the devices pages", () => {
  it("routes within this device's own window", () => {
    const onNavigate = vi.fn();

    render(<MultiDeviceTopic section="setup" onNavigate={onNavigate} />);
    fireEvent.click(screen.getByRole("button", { name: k("open.management") }));
    expect(onNavigate).toHaveBeenLastCalledWith("/federation/devices?section=management");
    fireEvent.click(screen.getByRole("button", { name: k("open.devices") }));
    expect(onNavigate).toHaveBeenLastCalledWith("/federation/devices");

    openTab("browse");
    fireEvent.click(screen.getByRole("button", { name: k("open.library") }));
    expect(onNavigate).toHaveBeenLastCalledWith("/federation");
    expect(openLocalView).not.toHaveBeenCalled();
  });

  it("switches back to this computer when the window shows a managed device", async () => {
    setReach({ isLocal: false, pureClient: true, console: true });
    vi.mocked(openLocalView).mockRejectedValueOnce(new Error("gone"));
    const onNavigate = vi.fn();

    render(<MultiDeviceTopic section="browse" onNavigate={onNavigate} />);
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: k("open.library") }));
    });

    expect(openLocalView).toHaveBeenCalledWith("/federation");
    expect(onNavigate).not.toHaveBeenCalled();
    expect(screen.getByRole("alert")).toHaveTextContent("federation.switcher.openFailed");
  });

  it("offers no link where those pages cannot open", () => {
    setReach({ isLocal: false });
    render(<MultiDeviceTopic section="setup" onNavigate={vi.fn()} />);
    expect(screen.queryByRole("button", { name: k("open.devices") })).not.toBeInTheDocument();
    cleanup();

    setReach({ isLocal: false, pureClient: true, console: false });
    render(<MultiDeviceTopic section="setup" onNavigate={vi.fn()} />);
    expect(screen.queryByRole("button", { name: k("open.management") })).not.toBeInTheDocument();
  });
});

describe("multi-device help topic: translations", () => {
  const placeholders = (text: string) => (text.match(/{{\s*\w+\s*}}/g) ?? []).sort();
  const en = { ...enHelpCenter, ...enFederation, ...enHelp } as Record<string, string>;
  const cn = { ...cnHelpCenter, ...cnFederation, ...cnHelp } as Record<string, string>;

  /** Renders every section, every interactive state and every concept, recording keys. */
  const renderEverything = async () => {
    used.clear();
    render(<MultiDeviceTopic onNavigate={vi.fn()} />);
    for (const device of devices) {
      fireEvent.click(document.querySelector(`[data-device="${device.id}"][role="button"]`)!);
    }
    for (const id of multiDeviceSections) openTab(id);
    openTab("browse");
    fireEvent.click(screen.getByRole("button", { name: "federation.scope.local" }));
    openTab("switch");
    fireEvent.click(document.querySelector('button[data-device="desktop"]')!);
    cleanup();

    for (const concept of multiDeviceConcepts) {
      render(<MultiDeviceConceptDetail conceptId={concept.id} />);
      cleanup();
    }

    // The failure note of a link that switches back to this computer.
    setReach({ isLocal: false, pureClient: true, console: true });
    vi.mocked(openLocalView).mockRejectedValueOnce(new Error("gone"));
    render(<MultiDeviceTopic section="browse" />);
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: k("open.library") }));
    });
    cleanup();

    // Keys the help center host reads from the registry rather than the topic.
    const topic = getHelpTopic("multiDevice");

    [
      topic.titleKey,
      topic.conceptGroupLabelKey!,
      ...topic.concepts!.map((c) => c.labelKey),
    ].forEach((key) => used.set(key, undefined));
  };

  it("uses only keys that exist in both English and Chinese", async () => {
    await renderEverything();

    expect(used.size).toBeGreaterThan(100);
    const missingEn = [...used.keys()].filter((key) => !(key in en));
    const missingCn = [...used.keys()].filter((key) => !(key in cn));

    expect(missingEn).toEqual([]);
    expect(missingCn).toEqual([]);

    for (const [key, options] of used) {
      for (const name of Object.keys(options ?? {})) {
        expect(en[key], key).toContain(`{{${name}}}`);
        expect(cn[key], key).toContain(`{{${name}}}`);
      }
    }
  });

  it("ships no key the topic never shows", async () => {
    await renderEverything();

    expect(Object.keys(enHelp).filter((key) => !used.has(key))).toEqual([]);
  });

  it("keeps the two languages in step", () => {
    expect(Object.keys(cnHelp).sort()).toEqual(Object.keys(enHelp).sort());
    for (const [key, text] of Object.entries(enHelp as Record<string, string>)) {
      const other = (cnHelp as Record<string, string>)[key];

      expect(text.trim(), key).not.toBe("");
      expect(other.trim(), key).not.toBe("");
      expect(placeholders(other), key).toEqual(placeholders(text));
    }
  });
});
