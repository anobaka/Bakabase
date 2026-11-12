import type { ReactNode } from "react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import AcquireDownloadForm from "../AcquireDownloadForm";
import type { AcquirePreflight, AcquireRequest } from "../models";
import cn from "@/locales/cn/components/acquire.json";
import en from "@/locales/en/components/acquire.json";
import { useDependentComponentContextsStore } from "@/stores/dependentComponentContexts";

const api = vi.hoisted(() => ({ preflight: vi.fn(), create: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({
  default: {
    downloadCenter: {
      preflightDownloadCenterCloud: api.preflight,
      createDownloadCenterCloudTasks: api.create,
    },
  },
}));
vi.mock("@/i18n", () => ({
  default: { t: (key: string) => key },
  getEnumKey: (type: string, name: string) =>
    `enum.${type[0].toLowerCase() + type.slice(1)}.${name[0].toLowerCase() + name.slice(1)}`,
}));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: { defaultValue?: string }) =>
      (cn as Record<string, string>)[key] ?? options?.defaultValue ?? key,
  }),
}));
vi.mock("@/stores/options", () => ({
  useDownloaderGlobalOptionsStore: (selector: (value: unknown) => unknown) =>
    selector({ data: { defaultDownloadPath: "/downloads" } }),
}));
vi.mock("../MethodPreparation", () => ({
  default: ({ methods, onAccountChange }: any) => (
    <div>
      {methods.map((method: any) => (
        <span key={method.id} data-testid="prepared-method">
          {method.id}
        </span>
      ))}
      <button type="button" onClick={() => onAccountChange("selected-account")}>
        Choose account fixture
      </button>
    </div>
  ),
}));
vi.mock("@/components/ThirdPartyConfig/base/DownloadDirectoryField", () => ({
  default: ({
    label,
    value,
    onChange,
  }: {
    label: string;
    value: string;
    onChange: (value: string) => void;
  }) => (
    <label>
      {label}
      <input value={value} onChange={(event) => onChange(event.target.value)} />
    </label>
  ),
}));
vi.mock("@/components/bakaui", () => ({
  Select: ({ label, selectedKeys, dataSource, isDisabled, onSelectionChange }: any) => (
    <select
      aria-label={label}
      value={selectedKeys[0] ?? ""}
      disabled={isDisabled}
      onChange={(event) => onSelectionChange(new Set([event.target.value]))}
    >
      {dataSource.map((item: any) => (
        <option key={item.value} value={item.value}>
          {item.label}
        </option>
      ))}
    </select>
  ),
  Button: ({
    children,
    onPress,
    isDisabled,
    type,
  }: {
    children: ReactNode;
    onPress: () => void;
    isDisabled?: boolean;
    type?: "button" | "submit";
  }) => (
    <button type={type ?? "button"} disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Input: ({
    label,
    value,
    onValueChange,
    isDisabled,
  }: {
    label: string;
    value: string;
    onValueChange: (value: string) => void;
    isDisabled?: boolean;
  }) => (
    <label>
      {label}
      <input
        value={value}
        disabled={isDisabled}
        onChange={(event) => onValueChange(event.target.value)}
      />
    </label>
  ),
  Checkbox: ({
    children,
    isSelected,
    onValueChange,
  }: {
    children: ReactNode;
    isSelected: boolean;
    onValueChange: (value: boolean) => void;
  }) => (
    <label>
      <input
        type="checkbox"
        checked={isSelected}
        onChange={(event) => onValueChange(event.target.checked)}
      />
      {children}
    </label>
  ),
}));

function ready(request: AcquireRequest): AcquirePreflight {
  return {
    mode: "files",
    canSubmit: !!request.candidates[0].url,
    requirements: [],
    candidates: request.candidates.map((candidate) => ({
      candidate,
      canSelect: true,
      state: "ready",
      requirements: [],
      methods: [
        {
          id: "googleDrive.gdown",
          displayName: "gdown",
          providerId: candidate.providerId!,
          transport: "site",
          supported: true,
          ready: true,
          state: "ready",
          requirements: [],
          dependencyIds: [],
          supportsTransfer: false,
        },
      ],
    })),
  };
}
beforeEach(() => {
  vi.clearAllMocks();
  useDependentComponentContextsStore.setState({ contexts: [] });
  api.preflight.mockImplementation(async (request: AcquireRequest) => ({
    code: 0,
    data: ready(request),
  }));
  api.create.mockResolvedValue({
    code: 0,
    data: { mode: "files", runs: [], resourceIds: [], acquisitionTaskIds: [] },
  });
});
const url = "https://drive.google.com/file/d/example/view";

it("embeds a download-only form and submits only to the download-center endpoint", async () => {
  const changed = vi.fn(),
    created = vi.fn();
  const { container } = render(
    <AcquireDownloadForm
      providerId="googleDrive"
      formId="download"
      onStateChange={changed}
      onCreated={created}
    />,
  );
  expect(container.querySelector('[role="dialog"]')).toBeNull();
  expect(container.textContent).not.toContain("获取并入库");
  expect(container.textContent).not.toContain("保存线索");
  expect(container.querySelector("select")).toBeNull();
  fireEvent.change(screen.getByLabelText("cloudDownload.url"), { target: { value: url } });
  await waitFor(() => expect(changed).toHaveBeenLastCalledWith({ canSubmit: true, busy: false }));
  fireEvent.submit(container.querySelector("form")!);
  await waitFor(() =>
    expect(api.create).toHaveBeenCalledExactlyOnceWith(
      expect.objectContaining({
        mode: "files",
        candidates: [expect.objectContaining({ providerId: "googleDrive", kind: "share", url })],
        selectedCandidateIds: ["download"],
      }),
      { outputDirectory: "/downloads" },
    ),
  );
  const payload = api.create.mock.calls[0][0];
  expect(payload).not.toHaveProperty("workflowDefinitionId");
  expect(payload.candidates[0]).not.toHaveProperty("extractionPlanJson");
  expect(payload.candidates[0]).not.toHaveProperty("archivePassword");
  await waitFor(() => expect(created).toHaveBeenCalledOnce());
});

it("localizes SOURCE_INVALID and refuses to submit an invalid source", async () => {
  api.preflight.mockImplementation(async (request: AcquireRequest) => ({
    code: 0,
    data: {
      ...ready(request),
      canSubmit: false,
      candidates: [
        {
          candidate: request.candidates[0],
          canSelect: false,
          state: "invalid",
          methods: [],
          requirements: [
            {
              code: "SOURCE_INVALID",
              kind: "source",
              message: "acquire.requirement.SOURCE_INVALID",
            },
          ],
        },
      ],
    },
  }));
  const changed = vi.fn();
  const { container } = render(
    <AcquireDownloadForm
      providerId="googleDrive"
      formId="download"
      onStateChange={changed}
      onCreated={vi.fn()}
    />,
  );
  fireEvent.change(screen.getByLabelText("cloudDownload.url"), {
    target: { value: "https://pan.baidu.com/s/not-drive" },
  });
  expect(await screen.findByText(cn["acquire.requirement.SOURCE_INVALID"])).toBeInTheDocument();
  expect(container.textContent).not.toContain("acquire.requirement.SOURCE_INVALID");
  fireEvent.submit(container.querySelector("form")!);
  expect(api.create).not.toHaveBeenCalled();
  expect(en["acquire.requirement.SOURCE_INVALID"]).not.toContain("acquire.requirement.");
});

it("explains unavailable integration before entering a link and cannot create a download", async () => {
  api.preflight.mockImplementation(async (request: AcquireRequest) => {
    const result = ready(request);
    return {
      code: 0,
      data: {
        ...result,
        candidates: result.candidates.map((candidate) => ({
          ...candidate,
          selectedMethodId: "terabox.cli",
          methods: [{ ...candidate.methods[0], id: "terabox.cli" }],
        })),
      },
    };
  });
  const changed = vi.fn();
  const { container } = render(
    <AcquireDownloadForm
      providerId="terabox"
      formId="download"
      onStateChange={changed}
      onCreated={vi.fn()}
    />,
  );
  expect(screen.getByRole("note")).toHaveTextContent("cloudDownload.teraboxIntegrationUnavailable");
  fireEvent.change(screen.getByLabelText("cloudDownload.url"), {
    target: { value: "https://www.terabox.com/s/fixture" },
  });
  await waitFor(() => expect(api.preflight).toHaveBeenCalled());
  await screen.findByTestId("prepared-method");
  expect(changed).toHaveBeenLastCalledWith({ canSubmit: false, busy: false });
  fireEvent.submit(container.querySelector("form")!);
  expect(api.create).not.toHaveBeenCalled();
});

it("shows the resolved source but submits the original address for server revalidation", async () => {
  const original = "https://cm1.hk/s/fixture";
  const resolved = "https://mypikpak.com/s/resolved-fixture";
  api.preflight.mockImplementation(async (request: AcquireRequest) => ({
    code: 0,
    data: {
      ...ready(request),
      candidates: ready(request).candidates.map((candidate) => ({
        ...candidate,
        resolvedSource: {
          id: candidate.candidate.id,
          url: resolved,
          originalUrl: original,
          providerId: "pikpak",
          kind: "share",
          resolutionChain: [original, resolved],
        },
      })),
    },
  }));
  const changed = vi.fn();
  const { container } = render(
    <AcquireDownloadForm
      providerId="pikpak"
      formId="download"
      onStateChange={changed}
      onCreated={vi.fn()}
    />,
  );
  fireEvent.change(screen.getByLabelText("cloudDownload.url"), { target: { value: original } });
  expect(await screen.findByText(resolved)).toBeInTheDocument();
  expect(screen.getByLabelText("cloudDownload.url")).toHaveValue(original);
  await waitFor(() => expect(changed).toHaveBeenLastCalledWith({ canSubmit: true, busy: false }));
  fireEvent.submit(container.querySelector("form")!);
  await waitFor(() =>
    expect(api.create).toHaveBeenCalledWith(
      expect.objectContaining({
        candidates: [expect.objectContaining({ url: original })],
      }),
      { outputDirectory: "/downloads" },
    ),
  );
  expect(api.create.mock.calls[0][0].candidates[0]).not.toHaveProperty("resolvedSource");
});

it("rejects late readiness for a previous address", async () => {
  let finishOld: (value: unknown) => void = () => {};
  api.preflight.mockImplementation((request: AcquireRequest) =>
    request.candidates[0].url.endsWith("old")
      ? new Promise((resolve) => {
          finishOld = resolve;
        })
      : Promise.resolve({ code: 0, data: ready(request) }),
  );
  const changed = vi.fn();
  render(
    <AcquireDownloadForm
      providerId="googleDrive"
      formId="download"
      onStateChange={changed}
      onCreated={vi.fn()}
    />,
  );
  const input = screen.getByLabelText("cloudDownload.url");
  fireEvent.change(input, { target: { value: url + "/old" } });
  await waitFor(() => expect(api.preflight).toHaveBeenCalled());
  fireEvent.change(input, { target: { value: url + "/new" } });
  await waitFor(() => expect(changed).toHaveBeenLastCalledWith({ canSubmit: true, busy: false }));
  finishOld({ code: 0, data: { ...ready(api.preflight.mock.calls[0][0]), canSubmit: false } });
  await waitFor(() => expect(changed).toHaveBeenLastCalledWith({ canSubmit: true, busy: false }));
});

it("does not submit twice while the first download request is pending", async () => {
  let complete: (value: unknown) => void = () => {};
  api.create.mockImplementation(
    () =>
      new Promise((resolve) => {
        complete = resolve;
      }),
  );
  const changed = vi.fn();
  const { container } = render(
    <AcquireDownloadForm
      providerId="googleDrive"
      formId="download"
      onStateChange={changed}
      onCreated={vi.fn()}
    />,
  );
  fireEvent.change(screen.getByLabelText("cloudDownload.url"), { target: { value: url } });
  await waitFor(() => expect(changed).toHaveBeenLastCalledWith({ canSubmit: true, busy: false }));
  fireEvent.submit(container.querySelector("form")!);
  fireEvent.submit(container.querySelector("form")!);
  expect(api.create).toHaveBeenCalledOnce();
  await waitFor(() => expect(changed).toHaveBeenLastCalledWith({ canSubmit: false, busy: true }));
  complete({ code: 0, data: { mode: "files", runs: [], resourceIds: [], acquisitionTaskIds: [] } });
  await waitFor(() => expect(changed).toHaveBeenLastCalledWith({ canSubmit: true, busy: false }));
});

it("checks and prepares only the chosen method, not an unavailable alternative", async () => {
  api.preflight.mockImplementation(async (request: AcquireRequest) => {
    const result = ready(request);
    const selectedMethodId = request.candidates[0].methodId ?? "pixeldrain.galleryDl";
    const requirements = [
      { code: "RCLONE_ACCOUNT_REQUIRED", kind: "account", message: "rclone account missing" },
    ];
    return {
      code: 0,
      data: {
        ...result,
        canSubmit: selectedMethodId === "pixeldrain.galleryDl",
        candidates: result.candidates.map((candidate) => ({
          ...candidate,
          selectedMethodId,
          requirements: selectedMethodId === "pixeldrain.galleryDl" ? [] : requirements,
          methods: [
            { ...candidate.methods[0], id: "pixeldrain.galleryDl" },
            {
              ...candidate.methods[0],
              id: "pixeldrain.rclone",
              engineId: "rclone",
              accountProvider: "pixeldrain",
              ready: false,
              requirements,
            },
          ],
        })),
      },
    };
  });
  const changed = vi.fn();
  render(
    <AcquireDownloadForm
      providerId="pixeldrain"
      formId="download"
      onStateChange={changed}
      onCreated={vi.fn()}
    />,
  );
  fireEvent.change(screen.getByLabelText("cloudDownload.url"), {
    target: { value: "https://pixeldrain.com/u/fixture" },
  });
  await waitFor(() => expect(changed).toHaveBeenLastCalledWith({ canSubmit: true, busy: false }));
  expect(screen.getByTestId("prepared-method")).toHaveTextContent("pixeldrain.galleryDl");
  expect(screen.queryByText(cn["acquire.requirement.RCLONE_ACCOUNT_REQUIRED"])).toBeNull();
  fireEvent.change(screen.getByLabelText(cn["acquire.method.label"]), {
    target: { value: "pixeldrain.rclone" },
  });
  await waitFor(() => expect(changed).toHaveBeenLastCalledWith({ canSubmit: false, busy: false }));
  expect(await screen.findByText(cn["acquire.requirement.RCLONE_ACCOUNT_REQUIRED"])).toBeVisible();
  expect(screen.getByTestId("prepared-method")).toHaveTextContent("pixeldrain.rclone");
});

it("selects a typed account-remote method and sends its account on the candidate", async () => {
  api.preflight.mockImplementation(async (request: AcquireRequest) => ({
    code: 0,
    data: {
      ...ready(request),
      candidates: ready(request).candidates.map((candidate) => ({
        ...candidate,
        selectedMethodId: request.candidates[0].methodId ?? "pikpak.share",
        methods: [
          { ...candidate.methods[0], id: request.candidates[0].methodId ?? "pikpak.share" },
        ],
      })),
    },
  }));
  const changed = vi.fn();
  const { container } = render(
    <AcquireDownloadForm
      providerId="pikpak"
      formId="download"
      onStateChange={changed}
      onCreated={vi.fn()}
    />,
  );
  fireEvent.click(screen.getByLabelText(cn["acquire.download.remote"]));
  fireEvent.change(screen.getByLabelText(cn["acquire.download.remotePath"]), {
    target: { value: "/folder" },
  });
  fireEvent.click(screen.getByText("Choose account fixture"));
  await waitFor(() => expect(changed).toHaveBeenLastCalledWith({ canSubmit: true, busy: false }));
  fireEvent.submit(container.querySelector("form")!);
  await waitFor(() =>
    expect(api.create).toHaveBeenCalledWith(
      expect.objectContaining({
        candidates: [
          expect.objectContaining({
            kind: "remote",
            providerId: "pikpak",
            methodId: "pikpak.rclone",
            accountId: "selected-account",
            url: "/folder",
          }),
        ],
      }),
      { outputDirectory: "/downloads" },
    ),
  );
  expect(api.create.mock.calls[0][0]).not.toHaveProperty("accountId");
});
