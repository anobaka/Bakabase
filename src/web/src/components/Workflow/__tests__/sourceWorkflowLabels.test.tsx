import { Children, isValidElement, type ReactNode } from "react";
import { createInstance } from "i18next";
import { I18nextProvider, initReactI18next } from "react-i18next";
import { act, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import LocalProcessingModal from "@/pages/post-parser/components/LocalProcessingModal";
import SourceExecutionSummary from "@/components/Acquire/SourceExecutionSummary";
import SourceExecutionHistory from "@/components/Acquire/SourceExecutionHistory";
import type { SourceRun, SourceRuns } from "@/components/Acquire/sourceHistory";
import type { BakabaseModulesWorkflowAbstractionsModelsViewWorkflowDefinitionViewModel as Definition } from "@/sdk/Api";
import { PostParserSource, WorkflowRunStatus } from "@/sdk/constants";
import cn from "@/locales/cn/pages/workflow.json";
import en from "@/locales/en/pages/workflow.json";
import cnAcquire from "@/locales/cn/components/acquire.json";
import enAcquire from "@/locales/en/components/acquire.json";

const api = vi.hoisted(() => ({ workflows: vi.fn(), history: vi.fn() }));
vi.mock("react-i18next", async (importOriginal) => await importOriginal());
vi.mock("@/sdk/BApi", () => ({
  default: {
    workflow: { searchWorkflows: api.workflows },
    workflowSources: { queryWorkflowSourceRuns: api.history },
  },
}));
vi.mock("react-router-dom", () => ({ useNavigate: () => vi.fn() }));
vi.mock("@/components/FileSystemSelector/components/Modal", () => ({ default: () => null }));
vi.mock("@/components/FileSystemSelector/components/Panel", () => ({ default: () => null }));
vi.mock("@/components/Workflow/WorkflowBrowserModal", () => ({ default: () => null }));
vi.mock("@/components/Workflow/WorkflowSummary", () => ({ default: () => null }));
vi.mock("@/components/Workflow/WorkflowStepCounts", () => ({ default: () => null }));
vi.mock("@/components/Workflow/WorkflowFileLocation", () => ({
  default: ({ path }: { path: string }) => <span>{path}</span>,
}));
vi.mock("@/components/Workflow/LocalFileWorkflowPreview", () => ({ default: () => null }));
vi.mock("@/components/ThirdPartyIcon", () => ({ default: () => null }));
vi.mock("@/components/ExternalLink", () => ({ default: () => null }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("@/pages/post-parser/components/DownloadGroupHeader", () => ({ default: () => null }));
vi.mock("../ResumeRunModal", () => ({ default: () => null }));
vi.mock("../RenamePlanPanel", () => ({ default: () => null }));
vi.mock("../displayNames", () => ({ activityDisplayName: (_: unknown, kind: string) => kind }));
vi.mock("@/components/CloudDownload/WorksetResults", () => ({ default: () => null }));
vi.mock("@heroui/react", () => {
  const Frame = ({ children }: { children: ReactNode }) => <div>{children}</div>;
  return {
    Drawer: Frame,
    DrawerBody: Frame,
    DrawerContent: Frame,
    DrawerHeader: Frame,
    DrawerFooter: Frame,
    SelectItem: Frame,
  };
});
vi.mock("@/components/bakaui", () => ({
  Accordion: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  AccordionItem: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  Modal: ({ children, footer }: { children: ReactNode; footer: ReactNode }) => (
    <div>
      {children}
      {footer}
    </div>
  ),
  Button: ({
    children,
    onPress,
    isDisabled,
  }: {
    children: ReactNode;
    onPress?: () => void;
    isDisabled?: boolean;
  }) => (
    <button disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Select: ({
    children,
    label,
    description,
    selectedKeys,
    onSelectionChange,
  }: {
    children: ReactNode;
    label: string;
    description: string;
    selectedKeys: string[];
    onSelectionChange: (keys: Set<string>) => void;
  }) => (
    <div>
      <label>
        {label}
        <select
          value={selectedKeys[0] ?? ""}
          onChange={(event) => onSelectionChange(new Set([event.target.value]))}
        >
          {Children.map(children, (child) =>
            isValidElement<{ children: ReactNode }>(child) ? (
              <option value={String(child.key)}>{child.props.children}</option>
            ) : (
              child
            ),
          )}
        </select>
      </label>
      <p>{description}</p>
    </div>
  ),
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  Input: () => null,
  Checkbox: () => null,
  Switch: () => null,
  Spinner: () => null,
  Pagination: () => null,
}));

const i18n = createInstance();
await i18n.use(initReactI18next).init({
  lng: "cn",
  fallbackLng: "en",
  keySeparator: false,
  resources: {
    cn: { translation: { ...cn, ...cnAcquire } },
    en: { translation: { ...en, ...enAcquire } },
  },
});
const show = (children: ReactNode) =>
  render(<I18nextProvider i18n={i18n}>{children}</I18nextProvider>);
const source = { module: "anySource", entityKind: "item", entityId: "41" };
const acquireName = "Acquire supported sources (v1)";
const projection = (isBuiltin: boolean, id = 675): SourceRun => ({
  workflowName: acquireName,
  workflowIsBuiltin: isBuiltin,
  run: {
    id,
    workflowDefinitionId: id,
    parameterSources: [],
    hasPlanSnapshot: false,
    planNodes: [],
    status: WorkflowRunStatus.Success,
    startedAt: "2026-10-20",
    inputCount: 1,
    outputCount: 1,
    outputPreviewTruncated: false,
    failedItemCount: 0,
    stepStats: [],
    nodeExecutions: [],
  },
  phase: "delivered",
  completedFileCount: 10,
  reused: false,
  downloadTaskIds: [],
});
const history = (runs: SourceRun[]): SourceRuns => ({ source, runCount: runs.length, runs });

beforeEach(async () => {
  vi.clearAllMocks();
  await i18n.changeLanguage("cn");
});

describe("source workflow name localization", () => {
  it("lists enabled local-plan workflows by input contract and translates only built-in options after a language switch", async () => {
    const name = "Process local files using extraction instructions";
    const definitions: Definition[] = [true, false].map((isBuiltin, index) => ({
      id: index + 1,
      name,
      isBuiltin,
      triggerKind: "fs.processingPlan",
      enabled: true,
      createdAt: "2026-10-20",
      parameters: [],
      activities: [],
    }));
    api.workflows.mockResolvedValue({ code: 0, data: definitions });
    show(
      <LocalProcessingModal
        task={{
          id: 41,
          revision: 3,
          source: PostParserSource.SoulPlus,
          link: "https://example.com/post/41",
          targets: [],
          results: {
            DownloadInfo: { resources: [{ link: "https://example.com/file" }] },
          },
        }}
      />,
    );
    await screen.findByRole("option", { name: "按方案处理本地文件" });
    expect(screen.getByRole("button", { name: "选择目录" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name })).toBeInTheDocument();
    expect(api.workflows).toHaveBeenCalledExactlyOnceWith({
      triggerKind: "fs.processingPlan",
      enabledOnly: true,
    });
    expect(screen.getByText(cn["workflow.processing.workflowHint"])).toBeInTheDocument();
    expect(screen.getByText(cn["workflow.processing.binding.introduction"])).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: cn["workflow.processing.startSelected"] }),
    ).toBeDisabled();
    await act(async () => {
      await i18n.changeLanguage("en");
    });
    expect(
      screen.getByRole("option", { name: "Process local files using a plan" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Choose directory" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name })).toBeInTheDocument();
    expect(screen.getByText(en["workflow.processing.workflowHint"])).toBeInTheDocument();
    expect(screen.getByText(en["workflow.processing.binding.introduction"])).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: en["workflow.processing.startSelected"] }),
    ).toBeDisabled();
    expect(api.workflows).toHaveBeenCalledTimes(1);
    expect(definitions[0].name).toBe(name);
  });

  it("localizes the source-row built-in name while preserving an identically named user workflow", async () => {
    show(
      <>
        <SourceExecutionSummary value={history([projection(true)])} onHistory={() => {}} />
        <SourceExecutionSummary value={history([projection(false, 676)])} onHistory={() => {}} />
      </>,
    );
    expect(screen.getByRole("button", { name: "获取支持的来源并入库" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: acquireName })).toBeInTheDocument();
    await act(async () => {
      await i18n.changeLanguage("en");
    });
    expect(
      screen.getByRole("button", { name: "Acquire supported sources and import" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: acquireName })).toBeInTheDocument();
  });

  it("preserves built-in provenance in history and updates translated names without refetching runs", async () => {
    api.history.mockResolvedValue({
      code: 0,
      data: [history([projection(true), projection(false, 676)])],
    });
    show(<SourceExecutionHistory source={source} onClose={() => {}} />);
    await screen.findByText("获取支持的来源并入库");
    expect(screen.getByText(acquireName)).toBeInTheDocument();
    await act(async () => {
      await i18n.changeLanguage("en");
    });
    expect(screen.getByText("Acquire supported sources and import")).toBeInTheDocument();
    expect(screen.getByText(acquireName)).toBeInTheDocument();
    expect(api.history).toHaveBeenCalledTimes(1);
  });
});
