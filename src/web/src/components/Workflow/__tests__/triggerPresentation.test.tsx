import type { ReactNode } from "react";
import type * as ReactI18Next from "react-i18next";
import type { WorkflowTriggerDescriptor } from "../triggerPresentation";

import { readFileSync } from "node:fs";
import { resolve } from "node:path";

import { createInstance } from "i18next";
import { createRoot, type Root } from "react-dom/client";
import { act } from "react-dom/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { workflowTriggerRegistry } from "../Triggers";
import { getTriggerActivationMode, triggerActivationModes } from "../triggerPresentation";
import TriggerUsageSummary from "../TriggerUsageSummary";
import WorkflowTriggerBadge from "../WorkflowTriggerBadge";

import TriggersSection from "@/components/HelpCenter/topics/workflow/TriggersSection";
import cn from "@/locales/cn/pages/workflowTriggers.json";
import en from "@/locales/en/pages/workflowTriggers.json";
import BApi from "@/sdk/BApi";
import { WorkflowActivationMode } from "@/sdk/constants";

const api = vi.hoisted(() => ({ getWorkflowTriggers: vi.fn() }));
vi.mock("@/sdk/BApi", async (importOriginal) => {
  const actual = await importOriginal<{ default: typeof BApi }>();

  return { default: { ...actual.default, workflow: { ...actual.default.workflow, ...api } } };
});
vi.mock("react-i18next", async (importOriginal) => ({
  ...(await importOriginal<typeof ReactI18Next>()),
  useTranslation: () => ({ t: i18n.t }),
}));
vi.mock("@/components/HelpCenter", () => ({
  HelpCenterButton: ({ label, section }: { label?: string; section?: string }) => (
    <button data-help-section={section}>{label ?? "Help"}</button>
  ),
}));
vi.mock("@/components/bakaui", () => ({
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  Card: ({ children }: { children: ReactNode }) => <article>{children}</article>,
  CardBody: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  Button: ({ as, href, children, onPress, isLoading }: any) =>
    as === "a" ? (
      <a href={href}>{children}</a>
    ) : (
      <button disabled={isLoading} onClick={onPress}>
        {children}
      </button>
    ),
  Spinner: () => <span>Loading</span>,
  Select: ({ dataSource, selectedKeys, onSelectionChange, label }: any) => (
    <select
      aria-label={label}
      value={Array.from(selectedKeys)[0] as string}
      onChange={(event) => onSelectionChange(new Set([event.target.value]))}
    >
      {dataSource.map((item: any) => (
        <option key={item.value} value={item.value}>
          {item.label}
        </option>
      ))}
    </select>
  ),
}));

const i18n = createInstance();
await i18n.init({
  lng: "en",
  fallbackLng: "en",
  keySeparator: false,
  resources: { en: { translation: en }, cn: { translation: cn } },
});

const descriptor = (
  overrides: Partial<WorkflowTriggerDescriptor> = {},
): WorkflowTriggerDescriptor => ({
  kind: "fs.manualScan",
  displayName: "Scan files",
  activationMode: WorkflowActivationMode.Manual,
  sourceModule: "fs",
  supportsManualRun: true,
  requiresManualPayload: false,
  payloadFields: [],
  ...overrides,
});

let container: HTMLDivElement;
let root: Root;
let server = 0;
beforeEach(() => {
  (globalThis as any).IS_REACT_ACT_ENVIRONMENT = true;
  BApi.baseUrl = `/test-${++server}`;
  api.getWorkflowTriggers.mockReset();
  container = document.createElement("div");
  document.body.append(container);
  root = createRoot(container);
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
});

async function settle() {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 15));
  });
}

describe("workflow trigger presentation contract", () => {
  it("matches the backend registration contract and translates each server explanation in both languages", () => {
    const backendRoot = resolve(__dirname, "../../../../..");
    const contract = readFileSync(
      resolve(backendRoot, "tests/Bakabase.Tests/WorkflowTriggerMetadataTests.cs"),
      "utf8",
    );
    const registeredKinds = [
      ...contract.matchAll(/\["([\w.]+)"\]\s*=\s*\(WorkflowActivationMode\./g),
    ].map((match) => match[1]);

    expect(registeredKinds.length).toBeGreaterThan(0);
    expect(Object.keys(workflowTriggerRegistry).sort()).toEqual(registeredKinds.sort());

    const sources = [
      ...[
        "FsManualScan",
        "FsScheduledScan",
        "FsWatch",
        "SubscriptionUpdated",
        "DownloaderCompleted",
      ].map((name) => `apps/Bakabase.Service/Components/Workflow/Triggers/${name}Trigger.cs`),
      "apps/Bakabase.Service/Components/Workflow/Resources/ResourceMaterializedTrigger.cs",
      "apps/Bakabase.Service/Components/Downloader/DownloadResultWorkflow.cs",
      "modules/Bakabase.Modules.Acquisition/Components/Workflow/AcquisitionRequestedTrigger.cs",
      "modules/Bakabase.Modules.Acquisition/Components/Workflow/AcquisitionStatusChangedTrigger.cs",
      "modules/Bakabase.Modules.Collection/Components/Workflow/CollectionMembersAddedTrigger.cs",
      "legacy/Bakabase.InsideWorld.Business/Components/PostParser/Workflow/PostParserWorkflow.cs",
    ];
    const keys = sources.flatMap((source) =>
      [
        ...readFileSync(resolve(backendRoot, source), "utf8").matchAll(
          /DescriptionKey\s*=>\s*"(workflow\.trigger\.[\w.]+)"/g,
        ),
      ].map((match) => match[1]),
    );

    expect(new Set(keys).size).toBe(registeredKinds.length);
    for (const key of keys) {
      expect((cn as Record<string, string>)[key], `cn: ${key}`).toBeTruthy();
      expect((en as Record<string, string>)[key], `en: ${key}`).toBeTruthy();
    }
  });

  it("provides translated input, setup and source navigation for every registered trigger UI", () => {
    expect(Object.values(workflowTriggerRegistry).length).toBeGreaterThan(0);
    for (const ui of Object.values(workflowTriggerRegistry)) {
      expect(ui.guide, ui.kind).toBeDefined();
      const guide = ui.guide!;

      expect(guide.sourceEntry.path).toMatch(/^\/[a-z-]+$/);
      expect(guide.helpTarget).toEqual({ topic: "workflow", section: "triggers" });
      for (const dictionary of [cn, en]) {
        for (const key of [guide.inputKey, guide.configureKey, guide.sourceEntry.labelKey]) {
          expect((dictionary as Record<string, string>)[key], `${ui.kind}: ${key}`).toBeTruthy();
        }
      }
    }
    for (const mode of triggerActivationModes) {
      expect(cn[`workflowTriggers.mode.${mode}`]).toBeTruthy();
      expect(en[`workflowTriggers.mode.${mode}`]).toBeTruthy();
    }
  });

  it("uses server activation modes independently of manual-run support and never guesses unknown modes", () => {
    for (const [mode, expected] of [
      [WorkflowActivationMode.Manual, "manual"],
      [WorkflowActivationMode.Module, "module"],
      [WorkflowActivationMode.SystemEvent, "systemEvent"],
      [WorkflowActivationMode.Schedule, "schedule"],
      [WorkflowActivationMode.Watch, "watch"],
    ] as const) {
      for (const supportsManualRun of [true, false]) {
        expect(
          getTriggerActivationMode(descriptor({ activationMode: mode, supportsManualRun })),
        ).toBe(expected);
      }
    }
    expect(getTriggerActivationMode()).toBe("unknown");
    expect(getTriggerActivationMode(descriptor({ activationMode: undefined }))).toBe("unknown");
    expect(
      getTriggerActivationMode(descriptor({ activationMode: 900 as WorkflowActivationMode })),
    ).toBe("unknown");
  });

  it("keeps server descriptions for an unknown trigger and only offers the generic help entry", async () => {
    await act(async () =>
      root.render(
        <TriggerUsageSummary
          trigger={descriptor({
            kind: "plugin.future",
            displayName: "Plugin trigger",
            activationMode: WorkflowActivationMode.Unknown,
            sourceModule: "Example plugin",
            descriptionKey: "plugin.future.missing",
            description: "A server-owned event boundary",
          })}
        />,
      ),
    );

    expect(container.textContent).toContain("Unknown activation");
    expect(container.textContent).toContain("A server-owned event boundary");
    expect(container.textContent).toContain("Example plugin");
    expect(container.querySelector("a")).toBeNull();
    expect(container.querySelector('[data-help-section="triggers"]')).not.toBeNull();
    expect(api.getWorkflowTriggers).not.toHaveBeenCalled();
  });

  it("loads the runtime directory once for simultaneous consumers without a QueryClientProvider, including unknown server triggers", async () => {
    api.getWorkflowTriggers.mockResolvedValue({
      code: 0,
      data: [
        descriptor(),
        descriptor({
          kind: "subscription.updated",
          displayName: "New subscription resources",
          sourceModule: "subscription",
          activationMode: WorkflowActivationMode.SystemEvent,
        }),
        descriptor({
          kind: "plugin.event",
          displayName: "Plugin event",
          sourceModule: "Plugin",
          activationMode: WorkflowActivationMode.Unknown,
          description: "Plugin event details",
        }),
      ],
    });
    await act(async () =>
      root.render(
        <>
          <TriggersSection />
          <WorkflowTriggerBadge triggerKind="subscription.updated" />
          <TriggerUsageSummary compact triggerKind="subscription.updated" />
        </>,
      ),
    );
    await settle();

    expect(api.getWorkflowTriggers).toHaveBeenCalledTimes(1);
    expect(container.querySelectorAll("article")).toHaveLength(3);
    expect(container.textContent).toContain("Plugin event details");
    expect(container.querySelector('article a[href="#/subscriptions"]')).not.toBeNull();
    const select = container.querySelector("select")!;

    await act(async () => {
      select.value = "systemEvent";
      select.dispatchEvent(new Event("change", { bubbles: true }));
    });
    expect(container.querySelectorAll("article")).toHaveLength(1);
    expect(container.querySelector("article")?.textContent).toContain("New subscription resources");
  });

  it("reports a failed directory request instead of an empty catalogue and supports explicit refresh", async () => {
    api.getWorkflowTriggers
      .mockRejectedValueOnce(new Error("Offline"))
      .mockResolvedValueOnce({ code: 0, data: [descriptor()] });
    await act(async () => root.render(<TriggersSection />));
    await settle();
    expect(container.querySelector('[role="alert"]')?.textContent).toContain("Could not load");
    expect(container.textContent).not.toContain("No registered triggers");
    const refresh = Array.from(container.querySelectorAll("button")).find(
      (button) => button.textContent === "Refresh directory",
    )!;

    await act(async () => refresh.click());
    await settle();
    expect(api.getWorkflowTriggers).toHaveBeenCalledTimes(2);
    expect(container.querySelector('[role="alert"]')).toBeNull();
    expect(container.querySelectorAll("article")).toHaveLength(1);
  });

  it("lets the hosting help dialog handle source navigation and dismissal", async () => {
    const onNavigate = vi.fn();

    await act(async () =>
      root.render(
        <TriggerUsageSummary
          showHelp={false}
          trigger={descriptor({
            kind: "subscription.updated",
            sourceModule: "subscription",
            activationMode: WorkflowActivationMode.SystemEvent,
          })}
          onNavigate={onNavigate}
        />,
      ),
    );
    const source = Array.from(container.querySelectorAll("button")).find(
      (button) => button.textContent === "Open Subscriptions",
    )!;

    await act(async () => source.click());
    expect(onNavigate).toHaveBeenCalledWith("/subscriptions");
    expect(container.querySelector('[data-help-section="triggers"]')).toBeNull();
  });
});
