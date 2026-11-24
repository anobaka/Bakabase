import type { ReactNode } from "react";
import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import CloudDownloadModal from "../CloudDownloadModal";
import cn from "@/locales/cn/components/acquire.json";
import en from "@/locales/en/components/acquire.json";
import { useDependentComponentContextsStore } from "@/stores/dependentComponentContexts";
import { DependentComponentStatus } from "@/sdk/constants";
import type {
  AcquireAdapter,
  AcquirePreflight,
  AcquireRequest,
  DownloadCandidate,
} from "@/components/Acquire/models";

const api = vi.hoisted(() => ({ searchWorkflows: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({ default: { workflow: api } }));
vi.mock("@/i18n", () => ({ default: { t: (key: string) => key } }));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: { defaultValue?: string }) =>
      key.startsWith("acquire.requirement.")
        ? ((cn as Record<string, string>)[key] ?? options?.defaultValue ?? key)
        : key,
  }),
}));
vi.mock("react-router-dom", () => ({ useNavigate: () => vi.fn() }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("@/components/Acquire/MethodPreparation", () => ({
  default: () => <button>Set up dependency</button>,
}));
vi.mock("@/stores/options", () => ({
  useDownloaderGlobalOptionsStore: (select: (value: unknown) => unknown) =>
    select({ data: { defaultDownloadPath: "/downloads" } }),
}));
vi.mock("@/components/ThirdPartyConfig/base/DownloadDirectoryField", () => ({
  default: () => null,
}));
vi.mock("@heroui/react", () => ({
  Select: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  SelectItem: () => null,
}));
vi.mock("@/components/bakaui", async () => ({
  ...(await vi.importActual<typeof import("@heroui/react")>("@heroui/react").then(({ Radio, RadioGroup }) => ({
    Radio,
    RadioGroup,
  }))),
  Select: ({ children }: { children: ReactNode }) => <div>{children}</div>,
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
    "aria-pressed": pressed,
  }: {
    children: ReactNode;
    onPress: () => void;
    isDisabled?: boolean;
    "aria-pressed"?: boolean;
  }) => (
    <button aria-pressed={pressed} disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Input: ({
    label,
    value,
    onValueChange,
  }: {
    label: string;
    value: string;
    onValueChange: (value: string) => void;
  }) => (
    <label>
      {label}
      <input value={value} onChange={(event) => onValueChange(event.target.value)} />
    </label>
  ),
  Textarea: ({
    label,
    value,
    onValueChange,
  }: {
    label: string;
    value: string;
    onValueChange: (value: string) => void;
  }) => (
    <label>
      {label}
      <textarea value={value} onChange={(event) => onValueChange(event.target.value)} />
    </label>
  ),
  Checkbox: ({
    children,
    isSelected,
    isDisabled,
    onValueChange,
    "aria-label": label,
  }: {
    children: ReactNode;
    isSelected: boolean;
    isDisabled?: boolean;
    onValueChange: (value: boolean) => void;
    "aria-label"?: string;
  }) => (
    <label>
      <input
        aria-label={label}
        type="checkbox"
        checked={isSelected}
        disabled={isDisabled}
        onChange={(event) => onValueChange(event.target.checked)}
      />
      {children}
    </label>
  ),
  Spinner: () => null,
}));

const candidate: DownloadCandidate = {
  id: "source-1",
  kind: "auto",
  url: "https://pan.baidu.com/s/example",
  accessCode: "abcd",
  archivePassword: "archive-secret",
  extractionPlanJson: '{"requirement":"unknown"}',
};
const ready = (request: AcquireRequest, available = true): AcquirePreflight => ({
  mode: request.mode,
  canSubmit: request.selectedCandidateIds.length > 0 && (available || request.mode === "lead"),
  requirements: [],
  candidates: request.candidates.map((candidate) => ({
    candidate,
    canSelect: available || request.mode === "lead",
    state: available ? "ready" : "needsSetup",
    requirements: [],
    methods: [
      {
        id: "baidu.pcs",
        displayName: "BaiduPCS-Go",
        providerId: "baidu",
        transport: "site",
        supported: true,
        ready: available,
        state: available ? "ready" : "needsSetup",
        dependencyIds: ["baidu"],
        supportsTransfer: true,
        requirements: available
          ? []
          : [
              {
                code: "missing",
                kind: "dependency",
                message: "Install dependency",
                actions: ["installDependency"],
              },
            ],
      },
    ],
  })),
});
const adapter = (available = true): AcquireAdapter => ({
  preflight: vi.fn(async (request) => ready(request, available)),
  submit: vi.fn(async (request) => ({
    mode: request.mode,
    runs: [],
    resourceIds: [],
    acquisitionTaskIds: [],
  })),
});
beforeEach(() => api.searchWorkflows.mockReset().mockResolvedValue({ code: 0, data: [] }));

it("keeps plan editing available for a blocked candidate and preflights only the explicit revised instructions", async () => {
  const transport = adapter();
  const revised = JSON.stringify({ requirement: "notRequired", steps: [] });
  vi.mocked(transport.preflight).mockImplementation(async (request) => {
    const complete = request.candidates[0].extractionPlanJson === revised;
    const response = ready(request);
    return {
      ...response,
      canSubmit: complete && request.selectedCandidateIds.length > 0,
      candidates: response.candidates.map((prepared) => ({
        ...prepared,
        canSelect: complete,
        processingPlan: {
          status: complete ? "notRequired" : "incomplete",
          stepCount: 0,
          blocksSubmission: !complete,
          issues: complete
            ? []
            : [
                {
                  code: "PROCESSING_PLAN_INCOMPLETE",
                  kind: "processing",
                  message: "Plan needs steps",
                  actions: [],
                },
              ],
        },
      })),
    };
  });
  render(<CloudDownloadModal candidates={[candidate]} adapter={transport} />);
  const selection = await screen.findByRole("checkbox", { name: candidate.url });
  expect(selection).toBeDisabled();
  fireEvent.click(screen.getByRole("button", { name: "acquire.processingPlan.edit" }));
  const input = screen.getByLabelText("workflow.processing.plan");
  expect(input).toHaveValue(candidate.extractionPlanJson);
  fireEvent.change(input, { target: { value: revised } });
  await waitFor(() => expect(selection).toBeEnabled());
  fireEvent.click(selection);
  await waitFor(() =>
    expect(screen.getByRole("button", { name: "acquire.confirm.files" })).toBeEnabled(),
  );
  fireEvent.click(screen.getByRole("button", { name: "acquire.confirm.files" }));
  await waitFor(() => expect(transport.submit).toHaveBeenCalledTimes(1));
  expect(vi.mocked(transport.submit).mock.calls[0][0].candidates[0].extractionPlanJson).toBe(
    revised,
  );
  expect(candidate.extractionPlanJson).toBe('{"requirement":"unknown"}');
});

describe("generic acquisition confirmation", () => {
  it("creates a prepared acquisition without choosing a template or starting an execution", async () => {
    const transport = adapter(true);
    vi.mocked(transport.submit).mockResolvedValue({ mode: "files", runs: [], resourceIds: [], acquisitionTaskIds: [42] });
    render(<CloudDownloadModal purpose="plan" title="Saved title" candidates={[candidate]} adapter={transport} />);
    expect(screen.queryByRole("button", { name: /acquire.mode.library/ })).toBeNull();
    expect(screen.queryByRole("button", { name: /acquire.mode.lead/ })).toBeNull();
    expect(api.searchWorkflows).not.toHaveBeenCalled();
    fireEvent.click(await screen.findByRole("checkbox", { name: candidate.url }));
    await waitFor(() => expect(screen.getByRole("button", { name: "acquire.plan.create" })).toBeEnabled());
    fireEvent.click(screen.getByRole("button", { name: "acquire.plan.create" }));
    await screen.findByRole("button", { name: "acquire.plan.openTask" });
    expect(transport.submit).toHaveBeenCalledWith(expect.objectContaining({ title: "Saved title", mode: "files", workflowDefinitionId: undefined }), "/downloads");
    expect(screen.queryByRole("button", { name: "acquire.openRun" })).toBeNull();
  });
  it("rechecks preparation after the selected method dependency is installed", async () => {
    useDependentComponentContextsStore.setState({ contexts: [] });
    let installed = false;
    const transport = adapter(false);
    vi.mocked(transport.preflight).mockImplementation(async (request) => ready(request, installed));
    render(<CloudDownloadModal candidates={[candidate]} adapter={transport} />);
    expect(await screen.findByRole("checkbox", { name: candidate.url })).toBeDisabled();
    installed = true;
    act(() =>
      useDependentComponentContextsStore.setState({
        contexts: [
          {
            id: "baidu",
            name: "Baidu",
            status: DependentComponentStatus.Installed,
            defaultLocation: "",
            isAvailableOnCurrentPlatform: true,
            isRequired: false,
            installationProgress: 0,
          },
        ],
      }),
    );
    await waitFor(() =>
      expect(screen.getByRole("checkbox", { name: candidate.url })).not.toBeDisabled(),
    );
    useDependentComponentContextsStore.setState({ contexts: [] });
  });

  it("preserves source metadata and requires an explicit content choice", async () => {
    const transport = adapter();
    render(<CloudDownloadModal candidates={[candidate]} adapter={transport} />);
    expect(screen.getByRole("button", { name: "acquire.confirm.files" })).toBeDisabled();
    fireEvent.click(await screen.findByRole("checkbox", { name: candidate.url }));
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "acquire.confirm.files" })).not.toBeDisabled(),
    );
    fireEvent.click(screen.getByRole("button", { name: "acquire.confirm.files" }));
    await waitFor(() =>
      expect(transport.submit).toHaveBeenCalledWith(
        expect.objectContaining({
          mode: "files",
          candidates: [candidate],
          selectedCandidateIds: [candidate.id],
        }),
        "/downloads",
      ),
    );
  });

  it("disables unavailable download selection while keeping setup actions usable", async () => {
    render(<CloudDownloadModal candidates={[candidate]} adapter={adapter(false)} />);
    expect(await screen.findByRole("checkbox", { name: candidate.url })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Set up dependency" })).not.toBeDisabled();
    expect(screen.getByText("Install dependency")).toBeInTheDocument();
  });

  it("allows saving a lead without installing a download tool or logging in", async () => {
    const transport = adapter(false);
    render(<CloudDownloadModal candidates={[candidate]} adapter={transport} />);
    await screen.findByRole("checkbox", { name: candidate.url });
    fireEvent.click(screen.getByRole("button", { name: /acquire.mode.lead/ }));
    await waitFor(() =>
      expect(screen.getByRole("checkbox", { name: candidate.url })).not.toBeDisabled(),
    );
    fireEvent.click(screen.getByRole("checkbox", { name: candidate.url }));
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "acquire.confirm.lead" })).not.toBeDisabled(),
    );
    fireEvent.click(screen.getByRole("button", { name: "acquire.confirm.lead" }));
    await waitFor(() =>
      expect(transport.submit).toHaveBeenCalledWith(
        expect.objectContaining({ mode: "lead", selectedCandidateIds: [candidate.id] }),
        "",
      ),
    );
  });

  it("does not submit a stale ready response after changing the purpose", async () => {
    const transport = adapter();
    render(<CloudDownloadModal candidates={[candidate]} adapter={transport} />);
    fireEvent.click(await screen.findByRole("checkbox", { name: candidate.url }));
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "acquire.confirm.files" })).not.toBeDisabled(),
    );
    vi.mocked(transport.preflight).mockImplementation(() => new Promise(() => {}));
    fireEvent.click(screen.getByRole("button", { name: /acquire.mode.library/ }));
    expect(screen.getByRole("button", { name: "acquire.confirm.library" })).toBeDisabled();
    expect(transport.submit).not.toHaveBeenCalled();
  });
});

it("localizes candidate and global requirement codes while preserving unknown diagnostic text", async () => {
  const transport = adapter(false);
  vi.mocked(transport.preflight).mockImplementation(async (request) => {
    const result = ready(request, false);
    result.candidates[0].requirements = [
      {
        code: "BAIDU_LOGIN_REQUIRED",
        kind: "account",
        message: "Choose a logged-in Baidu account or log in here.",
      },
      {
        code: "UNRECOGNIZED_TOOL_ERROR",
        kind: "dependency",
        message: "tool exited: E123 /original/detail",
      },
    ];
    result.requirements = [
      {
        code: "OUTPUT_DIRECTORY_REQUIRED",
        kind: "configuration",
        message: "Choose an absolute output directory.",
      },
    ];
    return result;
  });
  render(<CloudDownloadModal candidates={[candidate]} adapter={transport} />);
  expect(
    await screen.findByText(cn["acquire.requirement.BAIDU_LOGIN_REQUIRED"]),
  ).toBeInTheDocument();
  expect(screen.getByText(cn["acquire.requirement.OUTPUT_DIRECTORY_REQUIRED"])).toBeInTheDocument();
  expect(screen.getByText("tool exited: E123 /original/detail")).toBeInTheDocument();
  for (const code of [
    "BAIDU_LOGIN_REQUIRED",
    "DEPENDENCY_REQUIRED",
    "PLATFORM_UNSUPPORTED",
    "SOURCE_UNSUPPORTED",
    "OUTPUT_DIRECTORY_REQUIRED",
    "REMOTE_PATH_FILES_ONLY",
  ])
    for (const dictionary of [cn, en])
      expect(dictionary).toHaveProperty(`acquire.requirement.${code}`);
});
