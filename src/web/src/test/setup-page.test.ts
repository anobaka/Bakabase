import { fireEvent, waitFor } from "@testing-library/dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { readSetupPage } from "./embedded-setup-page";

const html = readSetupPage("ServerSetup");

type SetupStatus = {
  mode: "first-run" | "relocate" | "import";
  currentPath: string;
  canChooseTargetPath: boolean;
  canBrowse: boolean;
  isDocker: boolean;
  allowedOperations: string[];
  automaticMaintenance?: boolean;
};

const firstRun: SetupStatus = {
  mode: "first-run",
  currentPath: "/server/default-data",
  canChooseTargetPath: true,
  canBrowse: false,
  isDocker: false,
  allowedOperations: ["initialize", "import"],
};
const existing: SetupStatus = {
  ...firstRun,
  mode: "relocate",
  currentPath: "/server/current-data",
  allowedOperations: ["relocate", "import"],
};

const element = <T extends HTMLElement = HTMLElement>(id: string) =>
  document.getElementById(id) as T;
const input = (id: string) => element<HTMLInputElement>(id);
const click = (id: string) => element(id).click();
const enter = (id: string, value: string) => fireEvent.input(input(id), { target: { value } });
const chooseLanguage = (value: string) =>
  fireEvent.change(element<HTMLSelectElement>("language"), { target: { value } });
const chooseImport = (value: boolean) => {
  const radio = input(value ? "import" : "empty");

  radio.checked = true;
  fireEvent.input(radio);
};
const choosePathChange = (change: boolean) => {
  const usingCustomPath = !element("targetFields").hidden;

  if (change !== usingCustomPath) click("editDestination");
};
const listing = (currentPath: string, extra: Record<string, unknown> = {}) => ({
  currentPath,
  parentPath: "/server",
  roots: [{ name: "Root", path: "/" }],
  directories: [],
  truncated: false,
  ...extra,
});
const response = (data: unknown) => ({ ok: true, status: 200, json: async () => data }) as Response;

function deferred<T>() {
  let resolveValue!: (value: T) => void;
  const promise = new Promise<T>((resolve) => {
    resolveValue = resolve;
  });

  return { promise, resolve: resolveValue };
}

const settle = () => new Promise((resolve) => setTimeout(resolve, 0));
const navigate = vi.fn();

type SetupRequest = {
  operation: string;
  targetPath: string;
  sourcePath?: string;
  originalDataPath?: string;
  pathPreflightId?: string;
  pathPreviewId?: string;
  pathMappings?: { sourcePrefix: string; targetPrefix: string }[];
};
type ApiOverrides = {
  validate?: (request: SetupRequest) => unknown | Promise<unknown>;
  apply?: (request: SetupRequest) => unknown | Promise<unknown>;
  draft?: unknown;
  saveDraft?: (value: unknown) => unknown | Promise<unknown>;
  clearDraft?: () => unknown | Promise<unknown>;
  preflight?: (request?: SetupRequest) => unknown | Promise<unknown>;
  tree?: (url: URL) => unknown | Promise<unknown>;
  preview?: (value: {
    scanId: string;
    rules: { sourcePrefix: string; targetPrefix: string }[];
  }) => unknown | Promise<unknown>;
};

async function mountPage(
  status: SetupStatus = firstRun,
  directories: (url: URL) => unknown | Promise<unknown> = (url) =>
    listing(url.searchParams.get("path") || status.currentPath),
  overrides: ApiOverrides = {},
) {
  let scanRequest: SetupRequest | undefined;
  const fetchMock = vi.fn(async (path: string, options: RequestInit) => {
    const url = new URL(path, location.href);

    if (url.pathname === "/bakabase/setup/status") return response({ ...status });
    if (url.pathname === "/bakabase/setup/draft") {
      if (options.method === "GET") return response({ draft: overrides.draft ?? null });
      const value = JSON.parse(options.body as string);

      return response(
        overrides.saveDraft
          ? await overrides.saveDraft(value)
          : { draft: { id: "draft-one", ...value } },
      );
    }
    if (url.pathname === "/bakabase/setup/draft/clear")
      return response(overrides.clearDraft ? await overrides.clearDraft() : { draft: null });
    if (url.pathname === "/bakabase/setup/preflight") {
      const request = options.method === "POST" ? JSON.parse(options.body as string) : undefined;

      if (request) scanRequest = request;

      return response(
        overrides.preflight
          ? await overrides.preflight(request)
          : {
              id: "scan-one",
              phase: "ready",
              uniquePaths: 2,
              referenceCount: 4,
              sourcePath: scanRequest?.sourcePath,
              targetPath: scanRequest?.targetPath,
            },
      );
    }
    if (url.pathname === "/bakabase/setup/preflight/tree")
      return response(
        overrides.tree ? await overrides.tree(url) : { nodes: [], total: 0, offset: 0, limit: 200 },
      );
    if (url.pathname === "/bakabase/setup/preflight/preview") {
      const value = JSON.parse(options.body as string);

      return response(
        overrides.preview
          ? await overrides.preview(value)
          : {
              previewId: "preview-one",
              matchedPaths: 0,
              matchedReferences: 0,
              unmappedPaths: 2,
              unmappedReferences: 4,
              examples: [],
              warnings: [],
            },
      );
    }
    if (url.pathname === "/bakabase/setup/directories") return response(await directories(url));
    if (url.pathname === "/bakabase/setup/validate") {
      const body = JSON.parse(options.body as string);

      return response(
        overrides.validate ? await overrides.validate(body) : { valid: true, ...body },
      );
    }
    if (url.pathname === "/bakabase/setup/apply") {
      const body = JSON.parse(options.body as string);

      return response(
        overrides.apply ? await overrides.apply(body) : { monitorToken: "monitor-token" },
      );
    }
    throw new Error(`Unexpected setup request: ${options.method} ${path}`);
  });

  vi.stubGlobal("fetch", fetchMock);
  renderPage();
  await waitFor(() => expect(element("form")).toBeVisible());

  return fetchMock;
}

function renderPage() {
  const parsed = new DOMParser().parseFromString(html, "text/html");

  document.body.innerHTML = parsed.body.innerHTML;
  // jsdom does not implement modal dialogs; preserve the browser's open/closed contract.
  const dialog = element<HTMLDialogElement>("folderDialog");

  dialog.showModal = () => {
    dialog.open = true;
  };
  dialog.close = () => {
    dialog.open = false;
  };
  // Execute the unmodified embedded script; intercept only full-page navigation.
  new window.Function("location", parsed.querySelector("script")!.textContent!)({
    href: location.href,
    pathname: location.pathname,
    search: location.search,
    hash: location.hash,
    replace: navigate,
  });
}

beforeEach(() => {
  history.replaceState({}, "", "/bakabase/setup#setupToken=test-setup-token&lang=en");
  localStorage.clear();
  sessionStorage.clear();
  navigate.mockReset();
});

afterEach(() => {
  document.body.replaceChildren();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

const toPreflight = async () => {
  click("next");
  await waitFor(() => expect(element("preflightSection")).toBeVisible());
};
const toReview = async () => {
  click("next");
  await waitFor(() => expect(element("targetSection")).not.toBeVisible());
  if (!element("preflightSection").hidden) {
    await waitFor(() => expect(element("next")).toBeEnabled());
    click("next");
  }
  await waitFor(() => expect(element("reviewSection")).toBeVisible());
};
const backToTarget = () => {
  click("previous");
  if (!element("preflightSection").hidden) click("previous");
};
const flowCalls = (mock: ReturnType<typeof vi.fn>) =>
  mock.mock.calls.filter(([path]) => !path.includes("/draft") && !path.includes("/preflight"));
const selectSource = (path = "/imports/old-library") => {
  chooseImport(true);
  if (input("source").readOnly) click("manualSource");
  enter("source", path);
};
const selectTarget = (path = "/server/new-data") => {
  choosePathChange(true);
  if (input("target").readOnly) click("manualTarget");
  enter("target", path);
};

describe("standalone setup wizard", () => {
  it("opens with data and location together and shows two steps for a new library", async () => {
    const fetchMock = await mountPage();

    expect(element("title")).toHaveTextContent("Welcome to Bakabase");
    expect(element("dataSection")).toBeVisible();
    expect(element("targetSection")).toBeVisible();
    expect(element("reviewSection")).not.toBeVisible();
    expect(input("empty")).toBeChecked();
    expect(element("editDestination")).toHaveAttribute("aria-expanded", "false");
    expect(element("step1")).toHaveAttribute("aria-current", "step");
    expect(element("step2")).toBeDisabled();
    expect(element("step3")).not.toBeVisible();
    expect(element("previous")).not.toBeVisible();
    expect(element("submit")).not.toBeVisible();
    expect(element("back")).not.toBeVisible();
    expect(flowCalls(fetchMock)).toEqual([
      [
        "/bakabase/setup/status",
        expect.objectContaining({
          method: "GET",
          headers: { "X-Bakabase-Setup-Token": "test-setup-token" },
        }),
      ],
    ]);

    expect(element("importDestinationPath")).toHaveTextContent(firstRun.currentPath);
    expect(element("targetFields")).not.toBeVisible();
    expect(element("editDestination")).toHaveAccessibleName("Change location");
    click("editDestination");
    expect(element("targetFields")).toBeVisible();
    expect(element("editDestination")).toHaveAccessibleName("Use the default data directory");
    expect(element("editDestination")).toHaveAttribute("aria-expanded", "true");
    expect(input("target")).toHaveValue("");
    expect(input("target").readOnly).toBe(true);
    expect(element("browseTarget")).toBeVisible();
    expect(element("manualTarget")).toBeVisible();
    click("editDestination");
    expect(element("editDestination")).toHaveAttribute("aria-expanded", "false");
    expect(element("importDestinationPath")).toHaveTextContent(firstRun.currentPath);
    expect(element("next")).toBeEnabled();
    expect(flowCalls(fetchMock)).toHaveLength(1);
  });

  it("validates default initialization before review and only applies after the final confirmation", async () => {
    history.replaceState({}, "", "/bakabase/setup#setupToken=test-setup-token&lang=cn");
    const fetchMock = await mountPage();

    expect(element("title")).toHaveTextContent("欢迎使用 Bakabase");
    expect(element("emptyLabel")).toHaveTextContent("创建新数据");
    await toReview();
    expect(element("dataSection")).not.toBeVisible();
    expect(element("targetSection")).not.toBeVisible();
    expect(element("reviewTarget")).toHaveTextContent(firstRun.currentPath);
    expect(element("reviewSource")).toHaveTextContent("创建新数据");
    expect(element("executionHint")).toHaveTextContent("无需手动重启");
    expect(element("submit")).toHaveTextContent("开始使用");
    expect(fetchMock).toHaveBeenLastCalledWith(
      "/bakabase/setup/validate",
      expect.objectContaining({
        body: JSON.stringify({ operation: "initialize", targetPath: firstRun.currentPath }),
      }),
    );
    expect(fetchMock.mock.calls.some(([path]) => path.endsWith("/apply"))).toBe(false);
    click("submit");
    await waitFor(() => expect(navigate).toHaveBeenCalledOnce());
    expect(navigate).toHaveBeenCalledWith(
      "/bakabase/app/data-path/import/progress#token=monitor-token&lang=cn",
    );
    expect(sessionStorage.getItem("bakabase.import.monitorToken")).toBe("monitor-token");
  });

  it("returns directly from the first step when the current data and location are kept", async () => {
    const fetchMock = await mountPage(existing);

    expect(element("title")).toHaveTextContent("Data settings");
    expect(element("emptyLabel")).toHaveTextContent("Keep current data");
    expect(element("editDestination")).toHaveAccessibleName("Use the current data directory");
    choosePathChange(false);
    expect(element("importDestinationPath")).toHaveTextContent(existing.currentPath);
    expect(element("finish")).toBeVisible();
    expect(element("finish")).toHaveAttribute("href", "/bakabase/#/configuration");
    expect(element("next")).not.toBeVisible();
    expect(element("submit")).not.toBeVisible();
    expect(element("step2")).toBeDisabled();
    expect(flowCalls(fetchMock)).toHaveLength(1);
  });

  it("keeps explicit import intent and validates the source against the known current directory", async () => {
    let valid = false;
    const fetchMock = await mountPage({ ...existing, mode: "import" }, undefined, {
      validate: (body) =>
        valid ? { valid: true, ...body } : { valid: false, error: "Source database is missing" },
    });

    expect(input("import")).toBeChecked();
    click("manualSource");
    enter("source", "/imports/external-library");
    click("next");
    await waitFor(() => expect(element("error")).toHaveTextContent("Source database is missing"));
    expect(element("dataSection")).toBeVisible();
    expect(element("targetSection")).toBeVisible();
    expect(element("step2")).toBeDisabled();
    expect(input("source")).toHaveValue("/imports/external-library");
    expect(fetchMock).toHaveBeenLastCalledWith(
      "/bakabase/setup/validate",
      expect.objectContaining({
        body: JSON.stringify({
          operation: "import",
          targetPath: existing.currentPath,
          sourcePath: "/imports/external-library",
        }),
      }),
    );
    valid = true;
    await toPreflight();
    expect(element("error")).not.toBeVisible();
    click("previous");
    chooseImport(false);
    expect(element("finish")).toBeVisible();
  });

  it("offers the default copy destination on the source step and allows changing it before validation", async () => {
    const fetchMock = await mountPage();

    selectSource();
    expect(element("importDestinationPath")).toHaveTextContent(firstRun.currentPath);
    expect(element("step2")).toHaveTextContent("Check paths");
    expect(element("targetFields")).not.toBeVisible();
    click("editDestination");
    expect(element("targetFields")).toBeVisible();
    expect(flowCalls(fetchMock)).toHaveLength(1);
    selectTarget();
    await toReview();
    expect(fetchMock).toHaveBeenLastCalledWith(
      "/bakabase/setup/validate",
      expect.objectContaining({
        body: JSON.stringify({
          operation: "import",
          targetPath: "/server/new-data",
          sourcePath: "/imports/old-library",
          pathPreflightId: "scan-one",
          pathPreviewId: "preview-one",
          pathMappings: [],
        }),
      }),
    );
    expect(element("reviewSource")).toHaveTextContent("/imports/old-library");
    expect(element("reviewTarget")).toHaveTextContent("/server/new-data");
  });

  it("keeps the source editable when the default copy destination cannot be used", async () => {
    const fetchMock = await mountPage(firstRun, undefined, {
      validate: (request) =>
        request.targetPath === firstRun.currentPath
          ? { valid: false, error: "Directory is not empty" }
          : { valid: true, ...request },
    });

    selectSource();
    click("next");
    await waitFor(() => expect(element("error")).toHaveTextContent("Directory is not empty"));
    expect(element("dataSection")).toBeVisible();
    expect(element("editDestination")).toBeVisible();
    expect(callsTo(fetchMock, "/preflight")).toHaveLength(0);
    click("editDestination");
    selectTarget();
    await toPreflight();
    await waitFor(() => expect(element("previous")).toBeEnabled());
    click("previous");
    expect(element("sourceTitle")).toHaveFocus();
    expect(input("source")).toHaveValue("/imports/old-library");
    expect(input("target")).toHaveValue("/server/new-data");
  });

  it("uses the deployment destination directly on a first-run import with fixed storage", async () => {
    const fetchMock = await mountPage({ ...firstRun, canChooseTargetPath: false });

    selectSource();
    expect(element("importDestinationPath")).toHaveTextContent(firstRun.currentPath);
    expect(element("editDestination")).not.toBeVisible();
    expect(element("step2")).toHaveTextContent("Check paths");

    await toReview();
    expect(element("reviewTarget")).toHaveTextContent(firstRun.currentPath);
    expect(callsTo(fetchMock, "/apply")).toHaveLength(0);
  });

  it("explains automatic maintenance and opens the independent desktop progress host after confirmation", async () => {
    const fetchMock = await mountPage({ ...existing, automaticMaintenance: true }, undefined, {
      apply: () => ({
        monitorToken: "desktop-monitor-token",
        monitorUrl: "http://127.0.0.1:43210/app/data-path/import/progress",
        requiresRestart: false,
      }),
    });

    selectSource();
    await toReview();
    expect(element("executionHint")).toHaveTextContent("save and close the current app");
    expect(element("executionHint")).toHaveTextContent("reopen it");
    expect(element("executionHint")).not.toHaveTextContent("restart manually");
    expect(fetchMock.mock.calls.some(([path]) => path.endsWith("/apply"))).toBe(false);
    click("submit");
    await waitFor(() => expect(navigate).toHaveBeenCalledOnce());
    expect(navigate).toHaveBeenCalledWith(
      "http://127.0.0.1:43210/app/data-path/import/progress#token=desktop-monitor-token&lang=en",
    );
  });

  it("requires a distinct destination when the user opts into changing it", async () => {
    await mountPage(existing);
    expect(element("editDestination")).toHaveAttribute("aria-expanded", "true");
    expect(element("targetFields")).toBeVisible();
    expect(element("next")).toBeVisible();
    expect(element("next")).toBeDisabled();
    expect(element("finish")).not.toBeVisible();
    expect(input("target")).toHaveValue("");
    click("manualTarget");
    enter("target", "/server/new-data");
    expect(element("next")).toBeEnabled();
    enter("target", `  ${existing.currentPath}  `);
    expect(element("next")).toBeDisabled();
  });

  it("preserves inputs when going back or using completed steps, and invalidates review after an edit", async () => {
    await mountPage();
    selectTarget();
    await toReview();
    expect(element("step2")).toHaveAttribute("aria-current", "step");
    backToTarget();
    expect(element("targetSection")).toBeVisible();
    expect(input("target")).toHaveValue("/server/new-data");
    click("step1");
    expect(element("dataSection")).toBeVisible();
    expect(element("sourceTitle")).toHaveFocus();
    click("step2");
    expect(element("reviewSection")).toBeVisible();
    expect(element("confirmTitle")).toHaveFocus();
    click("step1");
    selectSource();
    expect(element("step2")).toBeDisabled();
    expect(element("step3")).toBeDisabled();
    expect(element("editDestination")).toHaveAttribute("aria-expanded", "true");
    expect(input("target")).toHaveValue("/server/new-data");
    click("editDestination");
    expect(element("targetFields")).not.toBeVisible();
    expect(element("importDestinationPath")).toHaveTextContent(firstRun.currentPath);
    click("editDestination");
    expect(input("target")).toHaveValue("/server/new-data");
    await toReview();
    expect(element("reviewSource")).toHaveTextContent("/imports/old-library");
    expect(element("submit")).toHaveTextContent("Confirm and import");
  });

  it.each([firstRun, existing])(
    "keeps import and location choices independent for $mode",
    async (status) => {
      const fetchMock = await mountPage(status);

      selectSource();
      selectTarget();
      await toReview();
      expect(element("reviewTarget")).toHaveTextContent("/server/new-data");
      backToTarget();
      choosePathChange(false);
      expect(element("step3")).toBeDisabled();
      await toReview();
      expect(fetchMock).toHaveBeenLastCalledWith(
        "/bakabase/setup/validate",
        expect.objectContaining({
          body: JSON.stringify({
            operation: "import",
            targetPath: status.currentPath,
            sourcePath: "/imports/old-library",
            pathPreflightId: "scan-one",
            pathPreviewId: "preview-one",
            pathMappings: [],
          }),
        }),
      );
      expect(element("reviewTarget")).toHaveTextContent(status.currentPath);
      backToTarget();
      choosePathChange(true);
      expect(input("target")).toHaveValue("/server/new-data");
      chooseImport(false);
      chooseImport(true);
      expect(input("source")).toHaveValue("/imports/old-library");
      expect(element("editDestination")).toHaveAttribute("aria-expanded", "true");
      expect(input("target")).toHaveValue("/server/new-data");
    },
  );

  it("shows the fixed deployment location alongside import and no-op choices", async () => {
    await mountPage({
      ...existing,
      mode: "import",
      canChooseTargetPath: false,
      isDocker: true,
      allowedOperations: ["import"],
    });
    click("manualSource");
    enter("source", "/import");
    expect(element("targetFields")).not.toBeVisible();
    expect(element("browseTarget")).not.toBeVisible();
    expect(element("manualTarget")).not.toBeVisible();
    expect(input("target").readOnly).toBe(true);
    expect(element("importDestinationPath")).toHaveTextContent(existing.currentPath);
    expect(element("editDestination")).not.toBeVisible();
    expect(element("targetSection")).toHaveTextContent(/deployment/i);
    await toReview();
    expect(element("reviewEffect")).toHaveTextContent("Back up current data, then replace it");
    click("step1");
    chooseImport(false);
    expect(element("finish")).toBeVisible();
  });

  it("submits the server-validated snapshot and describes combined imports as queued until restart", async () => {
    const fetchMock = await mountPage(existing, undefined, {
      validate: (body) => ({
        valid: true,
        ...body,
        targetPath: body.pathPreflightId ? body.targetPath : body.targetPath + "/canonical",
        sourcePath: "/canonical/source",
        originalDataPath: "/original/data",
      }),
    });

    selectSource();
    enter("original", "/original/data");
    selectTarget();
    await toReview();
    expect(element("reviewSource")).toHaveTextContent("/canonical/source");
    expect(element("reviewTarget")).toHaveTextContent("/server/new-data/canonical");
    expect(element("reviewEffect")).toHaveTextContent(
      "Both the current and source directories are retained",
    );
    expect(element("executionHint")).toHaveTextContent("Restart the Bakabase app or server");
    expect(fetchMock.mock.calls.some(([path]) => path.endsWith("/apply"))).toBe(false);
    click("submit");
    await waitFor(() => expect(navigate).toHaveBeenCalledOnce());
    expect(fetchMock).toHaveBeenLastCalledWith(
      "/bakabase/setup/apply",
      expect.objectContaining({
        body: JSON.stringify({
          operation: "import",
          targetPath: "/server/new-data/canonical",
          sourcePath: "/canonical/source",
          originalDataPath: "/original/data",
          pathPreflightId: "scan-one",
          pathPreviewId: "preview-one",
          pathMappings: [],
        }),
      }),
    );
  });

  it("describes a relocation clearly and never applies a changed unvalidated selection", async () => {
    const fetchMock = await mountPage(existing);

    selectTarget();
    await toReview();
    expect(element("reviewSource")).toHaveTextContent(existing.currentPath);
    expect(element("reviewEffect")).toHaveTextContent("The original is retained");
    expect(element("submit")).toHaveTextContent("Confirm and move");
    // Even a value change without an input event cannot reuse a prior validation snapshot.
    input("target").value = "/server/changed-after-validation";
    click("submit");
    expect(element("targetSection")).toBeVisible();
    expect(element("step2")).toBeDisabled();
    expect(fetchMock.mock.calls.some(([path]) => path.endsWith("/apply"))).toBe(false);
  });

  it("keeps the selected destination editable when validation fails", async () => {
    let valid = false;

    await mountPage(firstRun, undefined, {
      validate: (body) =>
        valid ? { valid: true, ...body } : { valid: false, error: "Directory is not empty" },
    });
    selectTarget();
    click("next");
    await waitFor(() => expect(element("error")).toHaveTextContent("Directory is not empty"));
    expect(element("targetSection")).toBeVisible();
    expect(element("reviewSection")).not.toBeVisible();
    expect(input("target")).toHaveValue("/server/new-data");
    expect(element("step2")).toBeDisabled();
    valid = true;
    await toReview();
    expect(element("error")).not.toBeVisible();
  });

  it("prevents navigation or duplicate requests while checking the final selection", async () => {
    const pending = deferred<unknown>();
    const fetchMock = await mountPage(firstRun, undefined, { validate: () => pending.promise });

    click("next");
    expect(element("next")).toBeDisabled();
    expect(element("next")).toHaveTextContent("Checking");
    expect(element("previous")).toBeDisabled();
    expect(element("step1")).toBeDisabled();
    expect(element("form")).toHaveAttribute("aria-busy", "true");
    expect(element("submit")).not.toBeVisible();
    click("next");
    expect(flowCalls(fetchMock)).toHaveLength(2);
    pending.resolve({ valid: true, targetPath: firstRun.currentPath });
    await waitFor(() => expect(element("reviewSection")).toBeVisible());
    expect(element("confirmTitle")).toHaveFocus();
    expect(element("submit")).toBeEnabled();
    expect(element("form")).toHaveAttribute("aria-busy", "false");
  });
});

describe("setup language", () => {
  it("lets a visitor read first-run access instructions in either language before receiving a setup token", async () => {
    history.replaceState({}, "", "/bakabase/setup#lang=en");
    const fetchMock = vi.fn(async () => ({ ok: false, status: 403, json: async () => ({}) }));

    vi.stubGlobal("fetch", fetchMock);
    renderPage();
    await waitFor(() => expect(element("loading")).toHaveTextContent("docker compose logs server"));
    expect(element("loading")).toHaveTextContent("startup log");
    chooseLanguage("cn");
    expect(element("loading")).toHaveTextContent("启动日志");
    expect(element("loading")).toHaveTextContent("docker compose logs server");
    expect(element("form")).not.toBeVisible();
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it.each([
    { link: "en-US", saved: "cn", browser: ["zh-CN"], expected: "en" },
    { link: "zh-Hans", saved: "en", browser: ["en-GB"], expected: "zh-CN" },
    { link: "ja", saved: "cn", browser: ["en-GB"], expected: "zh-CN" },
    { link: "", saved: "", browser: ["ja-JP", "zh-CN"], expected: "zh-CN" },
    { link: "", saved: "", browser: ["ja-JP", "fr"], expected: "en" },
  ])(
    "resolves $link / $saved / $browser to $expected",
    async ({ link, saved, browser, expected }) => {
      history.replaceState({}, "", "/bakabase/setup#setupToken=test-setup-token&lang=" + link);
      if (saved) localStorage.setItem("bakabase.setup.language", saved);
      vi.spyOn(navigator, "languages", "get").mockReturnValue(browser);
      vi.spyOn(navigator, "language", "get").mockReturnValue(browser[0]);
      await mountPage();

      expect(document.documentElement).toHaveAttribute("lang", expected);
      expect(element("language")).toHaveValue(expected === "en" ? "en" : "cn");
      expect(element("title")).toHaveTextContent(
        expected === "en" ? "Welcome to Bakabase" : "欢迎使用 Bakabase",
      );
    },
  );

  it("changes language while editing, then preserves the checked review and uses that language for progress", async () => {
    const fetchMock = await mountPage();

    selectSource("/incoming/收藏");
    selectTarget("/new/library");
    chooseLanguage("cn");

    expect(element("dataSection")).toBeVisible();
    expect(input("source")).toHaveValue("/incoming/收藏");
    expect(input("target")).toHaveValue("/new/library");
    expect(input("import")).toBeChecked();
    expect(element("editDestination")).toHaveAttribute("aria-expanded", "true");
    expect(input("source").readOnly).toBe(false);
    expect(element("source").getAttribute("placeholder")).toMatch(/目录/);
    expect(element("browseSource")).toHaveAccessibleName(/目录/);
    expect(document.title).toMatch(/设置/);
    expect(localStorage.getItem("bakabase.setup.language")).toBe("cn");
    const fragment = new URLSearchParams(location.hash.slice(1));

    expect(fragment.get("setupToken")).toBe("test-setup-token");
    expect(fragment.get("lang")).toBe("cn");
    expect(callsTo(fetchMock, "/validate")).toHaveLength(0);
    await toReview();
    expect(element("submit")).toHaveTextContent("确认并导入");
    const validated = callsTo(fetchMock, "/validate").length;
    const scanCount = callsTo(fetchMock, "/preflight").length;

    chooseLanguage("en");
    expect(element("submit")).toHaveTextContent("Confirm and import");
    expect(element("reviewSource")).toHaveTextContent("/incoming/收藏");
    expect(element("reviewTarget")).toHaveTextContent("/new/library");
    expect(element("reviewMappings")).toHaveTextContent("Keep the original paths");
    expect(callsTo(fetchMock, "/validate")).toHaveLength(validated);
    expect(callsTo(fetchMock, "/preflight")).toHaveLength(scanCount);
    click("submit");
    await waitFor(() => expect(navigate).toHaveBeenCalledOnce());
    expect(navigate).toHaveBeenCalledWith(
      "/bakabase/app/data-path/import/progress#token=monitor-token&lang=en",
    );
    expect(localStorage.length).toBe(1);
    expect(localStorage.getItem("bakabase.setup.language")).toBe("en");
  });

  it("translates a known source diagnostic without changing filenames", async () => {
    history.replaceState({}, "", "/bakabase/setup#setupToken=test-setup-token&lang=cn");
    const diagnostic = "The source must contain app.json and bakabase_insideworld.db.";

    await mountPage(firstRun, undefined, {
      validate: () => ({ valid: false, error: diagnostic }),
    });
    selectSource();
    click("next");
    await waitFor(() => expect(element("error")).toBeVisible());
    expect(element("error")).toHaveTextContent("app.json");
    expect(element("error")).toHaveTextContent("bakabase_insideworld.db");
    expect(element("error")).toHaveTextContent(/[\u4e00-\u9fff]/);
    expect(element("error")).not.toHaveTextContent(diagnostic);
  });

  it("translates connection errors in place and keeps the selection available for retry", async () => {
    const fetchMock = await mountPage(firstRun, undefined, {
      validate: () => {
        throw new TypeError("Failed to fetch");
      },
    });

    selectTarget("/storage/library");
    click("next");
    await waitFor(() => expect(element("error")).toHaveTextContent("Connection failed"));
    chooseLanguage("cn");
    expect(element("error")).toHaveTextContent("连接失败");
    expect(input("target")).toHaveValue("/storage/library");
    expect(element("next")).toBeEnabled();
    expect(callsTo(fetchMock, "/validate")).toHaveLength(1);
  });
});

describe("setup wizard directory picker", () => {
  it("uses a virtual storage-root view in Docker and keeps read-only sources selectable", async () => {
    const roots = [{ name: "Imported data", path: "/import", readOnly: true }];
    const fetchMock = await mountPage({ ...firstRun, isDocker: true }, (url) =>
      listing(url.searchParams.get("path") === "/import" ? "/import" : "", {
        parentPath: null,
        isRestricted: true,
        roots,
        directories: url.searchParams.get("path") === "/import" ? [] : roots,
      }),
    );

    chooseImport(true);
    click("browseSource");
    await waitFor(() => expect(element("folderPath")).toHaveTextContent("Storage locations"));
    expect(callsTo(fetchMock, "/directories")[0][0]).toBe("/bakabase/setup/directories");
    expect(element("folderSelect")).toBeDisabled();
    expect(element("folderUp")).toBeDisabled();
    expect(element("folderRoots")).toHaveTextContent("Imported data · Read-only");
    element("folderList").querySelector<HTMLButtonElement>("button")!.click();
    await waitFor(() => expect(element("folderSelect")).toBeEnabled());
    expect(element("folderUp")).toBeDisabled();
    click("folderSelect");
    await waitFor(() => expect(input("source")).toHaveValue("/import"));
  });

  it("explains missing mounts without allowing the virtual root to be selected", async () => {
    await mountPage({ ...firstRun, isDocker: true }, () =>
      listing("", { parentPath: null, isRestricted: true, roots: [] }),
    );
    chooseImport(true);
    click("browseSource");
    await waitFor(() =>
      expect(element("folderMessage")).toHaveTextContent(
        "No mounted storage directories were found",
      ),
    );
    expect(element("folderSelect")).toBeDisabled();
    expect(element("folderList").children).toHaveLength(0);
  });

  it("starts relocation browsing in the current directory's parent", async () => {
    const fetchMock = await mountPage(existing, (url) =>
      url.searchParams.get("path") === existing.currentPath
        ? listing(existing.currentPath, { parentPath: "/server" })
        : listing("/server", {
            parentPath: "/",
            directories: [{ name: "Another disk", path: "/server/other-data" }],
          }),
    );

    choosePathChange(true);
    click("browseTarget");
    await waitFor(() => expect(element("folderSelect")).toBeEnabled());
    expect(element("folderPath")).toHaveTextContent("/server");
    expect(element("folderPath")).not.toHaveTextContent(existing.currentPath);
    expect(element("folderList")).toHaveTextContent("Another disk");
    expect(
      flowCalls(fetchMock)
        .slice(1)
        .map(([path]) => new URL(path, location.href).searchParams.get("path")),
    ).toEqual([existing.currentPath, "/server"]);
    click("folderClose");
    expect(element("next")).toBeDisabled();
  });

  it("uses the server-returned destination and checks a candidate name without creating it", async () => {
    const fetchMock = await mountPage(firstRun, (url) =>
      listing(
        "/volumes/storage",
        url.searchParams.has("newFolderName")
          ? { candidatePath: "/volumes/storage/My library" }
          : {},
      ),
    );

    choosePathChange(true);
    click("browseTarget");
    await waitFor(() => expect(element("folderSelect")).toBeEnabled());
    expect(element("folderPath")).toHaveTextContent("/volumes/storage");
    enter("folderName", "My library");
    click("folderSelect");
    await waitFor(() => expect(input("target")).toHaveValue("/volumes/storage/My library"));
    expect(element<HTMLDialogElement>("folderDialog").open).toBe(false);
    expect(element("targetSection")).toBeVisible();
    expect(input("target").readOnly).toBe(true);
    const [path, options] = fetchMock.mock.calls[fetchMock.mock.calls.length - 1];
    const query = new URL(path, location.href).searchParams;

    expect(query.get("path")).toBe("/volumes/storage");
    expect(query.get("newFolderName")).toBe("My library");
    expect(options.method).toBe("GET");
    expect(
      fetchMock.mock.calls
        .filter(([path]) => !path.includes("/draft"))
        .every(([, request]) => request.method === "GET"),
    ).toBe(true);
  });

  it("browses existing source folders without offering a new-folder candidate", async () => {
    const fetchMock = await mountPage(firstRun, (url) =>
      url.searchParams.get("path") === "/mounted/old-data"
        ? listing("/mounted/old-data")
        : listing("/mounted", {
            directories: [{ name: "Old library", path: "/mounted/old-data" }],
          }),
    );

    chooseImport(true);
    click("browseSource");
    await waitFor(() => expect(element("folderSelect")).toBeEnabled());
    expect(element("folderNew")).not.toBeVisible();
    element("folderList").querySelector<HTMLButtonElement>("button")!.click();
    await waitFor(() => expect(element("folderPath")).toHaveTextContent("/mounted/old-data"));
    await waitFor(() => expect(element("folderSelect")).toBeEnabled());
    click("folderSelect");
    await waitFor(() => expect(input("source")).toHaveValue("/mounted/old-data"));
    expect(input("source").readOnly).toBe(true);
    expect(element("dataSection")).toBeVisible();
    expect(fetchMock.mock.calls.every(([path]) => !path.includes("newFolderName"))).toBe(true);
  });

  it.each(["button", "escape"])(
    "cancels with %s without losing the edited destination or step",
    async (method) => {
      await mountPage(firstRun, () => listing("/server/other-directory"));
      selectTarget("/server/my-choice");
      click("browseTarget");
      await waitFor(() => expect(element("folderSelect")).toBeEnabled());
      enter("folderName", "uncommitted");
      if (method === "button") click("folderClose");
      else element("folderDialog").dispatchEvent(new Event("cancel", { cancelable: true }));
      expect(element<HTMLDialogElement>("folderDialog").open).toBe(false);
      expect(input("target")).toHaveValue("/server/my-choice");
      expect(input("target").readOnly).toBe(false);
      expect(element("targetSection")).toBeVisible();
      expect(element("next")).toBeEnabled();
    },
  );

  it("ignores a selection response received after the picker was cancelled", async () => {
    const pending = deferred<ReturnType<typeof listing>>();

    await mountPage(firstRun, (url) =>
      url.searchParams.has("newFolderName") ? pending.promise : listing("/server/parent"),
    );
    choosePathChange(true);
    click("browseTarget");
    await waitFor(() => expect(element("folderSelect")).toBeEnabled());
    enter("folderName", "late-result");
    click("folderSelect");
    expect(element("folderSelect")).toBeDisabled();
    click("folderClose");
    click("manualTarget");
    enter("target", "/server/kept-choice");
    pending.resolve(listing("/server/parent", { candidatePath: "/server/parent/late-result" }));
    await settle();
    expect(element<HTMLDialogElement>("folderDialog").open).toBe(false);
    expect(input("target")).toHaveValue("/server/kept-choice");
    expect(input("target").readOnly).toBe(false);
  });

  it("ignores an old listing after closing and reopening the picker", async () => {
    const pending = deferred<ReturnType<typeof listing>>();
    let loads = 0;

    await mountPage(firstRun, () =>
      ++loads === 1 ? pending.promise : listing("/server/new-listing"),
    );
    choosePathChange(true);
    click("browseTarget");
    expect(element("folderSelect")).toBeDisabled();
    click("folderClose");
    click("browseTarget");
    await waitFor(() => expect(element("folderPath")).toHaveTextContent("/server/new-listing"));
    pending.resolve(
      listing("/server/stale-listing", {
        directories: [{ name: "Stale folder", path: "/server/stale-listing/child" }],
      }),
    );
    await settle();
    expect(element<HTMLDialogElement>("folderDialog").open).toBe(true);
    expect(element("folderPath")).toHaveTextContent("/server/new-listing");
    expect(element("folderList")).not.toHaveTextContent("Stale folder");
    expect(input("target")).toHaveValue("");
  });
});

const readyScan = { id: "scan-one", phase: "ready", uniquePaths: 4, referenceCount: 9 };
const rootNode = { path: "C:/Media", name: "C:/Media", referenceCount: 9, hasChildren: true };
const treeResponse = (nodes: unknown[], extra: Record<string, unknown> = {}) => ({
  nodes,
  total: nodes.length,
  offset: 0,
  limit: 200,
  ...extra,
});
const choosePrefix = (path: string) =>
  [...document.querySelectorAll<HTMLButtonElement>(".tree-node")]
    .find((button) => button.dataset.path === path)!
    .click();
const mappingInput = (source: string) =>
  [...element("ruleList").querySelectorAll<HTMLInputElement>("input")].find(
    (item) => item.dataset.source === source,
  )!;
const enterMapping = (source: string, target: string) =>
  fireEvent.input(mappingInput(source), { target: { value: target } });
const callsTo = (mock: ReturnType<typeof vi.fn>, suffix: string) =>
  mock.mock.calls.filter(([path]) => new URL(path, location.href).pathname.endsWith(suffix));

async function importPreflight(overrides: ApiOverrides = {}) {
  const fetchMock = await mountPage(firstRun, undefined, {
    tree: () => treeResponse([rootNode]),
    ...overrides,
  });

  selectSource();
  await toPreflight();
  await waitFor(() => expect(element("mappingEditor")).toBeVisible());

  return fetchMock;
}

describe("import path preflight", () => {
  it("requires a scan-backed preview even when keeping all original paths", async () => {
    const fetchMock = await importPreflight();

    expect(element("step2")).toHaveTextContent("2Check paths");
    expect(element("step3")).toHaveTextContent("3Review");
    expect(element("step2")).toHaveAttribute("aria-current", "step");
    expect(element("step3")).toBeDisabled();
    expect(element("submit")).not.toBeVisible();
    expect(element("noRules")).toHaveTextContent("original paths are kept");
    expect(callsTo(fetchMock, "/apply")).toHaveLength(0);
    click("next");
    await waitFor(() => expect(element("reviewSection")).toBeVisible());
    expect(element("step3")).toHaveAttribute("aria-current", "step");
    expect(element("reviewMappings")).toHaveTextContent("Keep the original paths unchanged");
    expect(JSON.parse(callsTo(fetchMock, "/preflight/preview")[0][1].body)).toEqual({
      scanId: "scan-one",
      rules: [],
    });
    click("submit");
    await waitFor(() => expect(navigate).toHaveBeenCalledOnce());
    expect(JSON.parse(callsTo(fetchMock, "/apply")[0][1].body)).toMatchObject({
      pathPreflightId: "scan-one",
      pathPreviewId: "preview-one",
      pathMappings: [],
    });
  });

  it("loads the path tree lazily and paginates a wide directory instead of rendering all paths", async () => {
    const fetchMock = await importPreflight({
      tree: (url) => {
        if (!url.searchParams.has("parent")) return treeResponse([rootNode]);
        const offset = Number(url.searchParams.get("offset"));

        return treeResponse(
          Array.from({ length: Math.min(200, 501 - offset) }, (_, i) => ({
            path: `C:/Media/Folder${offset + i}`,
            name: `Folder${offset + i}`,
            referenceCount: 1,
            hasChildren: false,
          })),
          { offset, total: 501 },
        );
      },
    });

    await waitFor(() => expect(element("pathTree")).toHaveTextContent("C:/Media"));
    expect(element("pathTree").querySelectorAll(".tree-node")).toHaveLength(1);
    expect(callsTo(fetchMock, "/preflight/tree")).toHaveLength(1);
    element("pathTree").querySelector<HTMLButtonElement>(".tree-toggle")!.click();
    await waitFor(() =>
      expect(element("pathTree").querySelectorAll(".tree-node")).toHaveLength(201),
    );
    expect(element("pathTree")).not.toHaveTextContent("Folder200");
    [...element("pathTree").querySelectorAll<HTMLButtonElement>("button")]
      .find((button) => button.textContent === "Load more directories")!
      .click();
    await waitFor(() =>
      expect(element("pathTree").querySelectorAll(".tree-node")).toHaveLength(401),
    );
    expect(
      new URL(callsTo(fetchMock, "/preflight/tree").at(-1)![0], location.href).searchParams.get(
        "offset",
      ),
    ).toBe("200");
  });

  it("previews parent and longer child rules, keeps escaped samples inert, and applies the checked rules", async () => {
    const fetchMock = await importPreflight({
      tree: (url) =>
        treeResponse(
          url.searchParams.has("parent")
            ? [{ path: "C:/Media/Photos", name: "Photos", referenceCount: 3, hasChildren: false }]
            : [rootNode],
        ),
      preview: () => ({
        previewId: "mapped-preview",
        matchedPaths: 3,
        matchedReferences: 7,
        unmappedPaths: 1,
        unmappedReferences: 2,
        examples: [
          {
            sourcePath: "C:/Media/<img src=x>",
            targetPath: "/media/<img src=x>",
            referenceCount: 2,
          },
        ],
        warnings: ["One destination is unavailable"],
      }),
    });

    await waitFor(() => expect(element("pathTree")).toHaveTextContent("C:/Media"));
    choosePrefix("C:/Media");
    expect(element("next")).toBeDisabled();
    enterMapping("C:/Media", "/media");
    element("pathTree").querySelector<HTMLButtonElement>(".tree-toggle")!.click();
    await waitFor(() => expect(element("pathTree")).toHaveTextContent("Photos"));
    choosePrefix("C:/Media/Photos");
    enterMapping("C:/Media/Photos", "/photos");
    expect(element("rulePriority")).toHaveTextContent("longest prefix");
    click("previewMappings");
    await waitFor(() => expect(element("mappingPreview")).toBeVisible());
    expect(element("previewCounts")).toHaveTextContent("3 paths and 7 references");
    expect(element("previewWarnings")).toHaveTextContent("One destination is unavailable");
    expect(element("previewSamples").querySelector("img")).toBeNull();
    expect(element("previewSamples")).toHaveTextContent("<img src=x>");
    click("next");
    await waitFor(() => expect(element("reviewSection")).toBeVisible());
    expect(element("reviewMappings")).toHaveTextContent("C:/Media/Photos → /photos");
    click("submit");
    await waitFor(() => expect(navigate).toHaveBeenCalledOnce());
    expect(JSON.parse(callsTo(fetchMock, "/apply")[0][1].body)).toMatchObject({
      pathPreviewId: "mapped-preview",
      pathMappings: [
        { sourcePrefix: "C:/Media", targetPrefix: "/media" },
        { sourcePrefix: "C:/Media/Photos", targetPrefix: "/photos" },
      ],
    });
  });

  it("localizes known preview warnings in Chinese and preserves unknown diagnostic text", async () => {
    history.replaceState({}, "", "/bakabase/setup#setupToken=test-setup-token&lang=cn");
    const fetchMock = await importPreflight({
      preview: () => ({
        previewId: "cn-preview",
        matchedPaths: 0,
        matchedReferences: 0,
        unmappedPaths: 4,
        unmappedReferences: 9,
        examples: [],
        warnings: [
          "Unmapped references will keep their original paths. Their files may be unavailable on this device.",
          "Destination availability is not checked here. Confirm the server/container mounts using the folder browser; no media files will be moved or folders created.",
          "Unknown scanner diagnostic: code 42",
        ],
      }),
    });

    click("previewMappings");
    await waitFor(() => expect(element("mappingPreview")).toBeVisible());
    expect(element("previewWarnings")).toHaveTextContent(
      "未匹配替换规则的引用会保留原路径，这些文件在当前设备上可能无法访问。",
    );
    expect(element("previewWarnings")).toHaveTextContent(
      "这里不会检查目标目录是否可用。请通过文件夹浏览器确认服务端或容器中的挂载位置；不会搬移媒体文件或创建文件夹。",
    );
    expect(element("previewWarnings")).toHaveTextContent("Unknown scanner diagnostic: code 42");
    expect(element("previewWarnings")).not.toHaveTextContent("Unmapped references");
    expect(element("previewWarnings")).not.toHaveTextContent("Destination availability");

    chooseLanguage("en");
    expect(element("scanStatus")).toHaveTextContent("Scan complete");
    expect(element("previewWarnings")).toHaveTextContent("Unmapped references");
    expect(element("previewWarnings")).toHaveTextContent("Destination availability");
    expect(element("previewWarnings")).toHaveTextContent("Unknown scanner diagnostic: code 42");
    expect(element("mappingPreview")).toBeVisible();
    expect(callsTo(fetchMock, "/preflight/preview")).toHaveLength(1);
  });

  it("chooses an existing server-side mapping destination without creating a folder", async () => {
    const fetchMock = await importPreflight();

    await waitFor(() => expect(element("pathTree")).toHaveTextContent("C:/Media"));
    choosePrefix("C:/Media");
    element("ruleList").querySelector<HTMLButtonElement>("button")!.click();
    await waitFor(() => expect(element("folderSelect")).toBeEnabled());
    expect(element("folderNew")).not.toBeVisible();
    expect(element("folderContext")).toHaveTextContent("existing media folder");
    click("folderSelect");
    await waitFor(() => expect(mappingInput("C:/Media")).toHaveValue(firstRun.currentPath));
    expect(element("preflightSection")).toBeVisible();
    expect(
      callsTo(fetchMock, "/directories").every(([path]) => !path.includes("newFolderName")),
    ).toBe(true);
  });

  it("ignores an old preview after a source change and requires another scan", async () => {
    const pending = deferred<unknown>();
    const fetchMock = await importPreflight({ preview: () => pending.promise });

    click("previewMappings");
    // Programmatic edits represent external changes while the request is in flight.
    enter("source", "/imports/changed-library");
    pending.resolve({
      previewId: "stale",
      matchedPaths: 4,
      matchedReferences: 9,
      unmappedPaths: 0,
      unmappedReferences: 0,
      examples: [],
      warnings: [],
    });
    await waitFor(() => expect(element("dataSection")).toBeVisible());
    await waitFor(() => expect(element("next")).toBeEnabled());
    expect(element("mappingPreview")).not.toBeVisible();
    expect(element("step3")).toBeDisabled();
    await toPreflight();
    expect(JSON.parse(callsTo(fetchMock, "/preflight").at(-1)![1].body).sourcePath).toBe(
      "/imports/changed-library",
    );
    expect(callsTo(fetchMock, "/apply")).toHaveLength(0);
  });

  it("invalidates a checked preview when the storage directory changes", async () => {
    const fetchMock = await importPreflight();

    click("next");
    await waitFor(() => expect(element("reviewSection")).toBeVisible());
    click("step1");
    choosePathChange(true);
    click("manualTarget");
    enter("target", "/storage/changed");
    expect(element("step2")).toBeDisabled();
    expect(element("step3")).toBeDisabled();
    await toPreflight();
    await waitFor(() => expect(element("mappingEditor")).toBeVisible());
    expect(element("mappingPreview")).not.toBeVisible();
    expect(JSON.parse(callsTo(fetchMock, "/preflight").at(-1)![1].body).targetPath).toBe(
      "/storage/changed",
    );
    expect(callsTo(fetchMock, "/apply")).toHaveLength(0);
  });

  it("ignores a late tree page from the previous scan", async () => {
    const pending = deferred<unknown>();
    let loads = 0;

    await importPreflight({
      tree: () =>
        ++loads === 1
          ? pending.promise
          : treeResponse([{ ...rootNode, path: "/new", name: "New scan" }]),
    });
    click("scanRetry");
    await waitFor(() => expect(element("pathTree")).toHaveTextContent("New scan"));
    pending.resolve(treeResponse([{ ...rootNode, name: "Stale scan" }]));
    await settle();
    expect(element("pathTree")).not.toHaveTextContent("Stale scan");
    expect(element("pathTree").querySelectorAll(".tree-node")).toHaveLength(1);
  });

  it("can rescan ready data while preserving rules and invalidating the previous preview", async () => {
    let scans = 0;
    const fetchMock = await importPreflight({
      preflight: () => ({ ...readyScan, id: `scan-${++scans}` }),
    });

    await waitFor(() => expect(element("pathTree")).toHaveTextContent("C:/Media"));
    choosePrefix("C:/Media");
    enterMapping("C:/Media", "/media");
    click("next");
    await waitFor(() => expect(element("reviewSection")).toBeVisible());
    click("previous");
    expect(element("scanRetry")).toBeVisible();
    click("scanRetry");
    await waitFor(() => expect(element("next")).toBeEnabled());
    expect(mappingInput("C:/Media")).toHaveValue("/media");
    expect(element("mappingPreview")).not.toBeVisible();
    expect(element("step3")).toBeDisabled();
    const post = callsTo(fetchMock, "/preflight").filter(
      ([, options]) => options.method === "POST",
    );

    expect(new URL(post.at(-1)![0], location.href).searchParams.get("force")).toBe("true");
    click("next");
    await waitFor(() => expect(element("reviewSection")).toBeVisible());
    expect(JSON.parse(callsTo(fetchMock, "/preflight/preview").at(-1)![1].body).scanId).toBe(
      "scan-2",
    );
    expect(callsTo(fetchMock, "/apply")).toHaveLength(0);
  });

  it("keeps scan progress during a temporary disconnection and resumes polling", async () => {
    let polls = 0;

    await mountPage(firstRun, undefined, {
      preflight: (request) => {
        if (request)
          return {
            id: "scan-one",
            phase: "scanning",
            progress: { scannedTextValues: 250, pathReferences: 40 },
          };
        if (++polls === 1) throw new Error("Connection lost");

        return readyScan;
      },
    });
    selectSource();
    await toPreflight();
    await waitFor(() => expect(element("scanStatus")).toHaveTextContent("250"));
    await waitFor(() => expect(element("scanError")).toHaveTextContent("reconnecting"), {
      timeout: 2500,
    });
    expect(element("scanStatus")).toHaveTextContent("250");
    expect(element("next")).toBeDisabled();
    await waitFor(() => expect(element("mappingEditor")).toBeVisible(), { timeout: 2500 });
    expect(element("scanError")).not.toBeVisible();
    expect(element("next")).toBeEnabled();
  });

  it("shows asynchronous scanning progress and offers a retry after a reported failure", async () => {
    let requests = 0;
    const fetchMock = await mountPage(firstRun, undefined, {
      preflight: (request) => {
        if (request && ++requests > 1) return readyScan;
        if (request)
          return {
            id: "scan-one",
            phase: "scanning",
            progress: { table: "ResourceCaches", scannedTextValues: 250, pathReferences: 40 },
          };

        return { id: "scan-one", phase: "failed", error: "Source disk is unavailable" };
      },
    });

    selectSource();
    await toPreflight();
    await waitFor(() => expect(element("scanStatus")).toHaveTextContent("ResourceCaches"));
    expect(element("scanStatus")).toHaveTextContent("250");
    expect(element("next")).toBeDisabled();
    await waitFor(
      () => expect(element("scanError")).toHaveTextContent("Source disk is unavailable"),
      { timeout: 2500 },
    );
    expect(element("scanRetry")).toBeVisible();
    click("scanRetry");
    await waitFor(() => expect(element("mappingEditor")).toBeVisible());
    expect(
      callsTo(fetchMock, "/preflight").filter(([, options]) => options.method === "POST"),
    ).toHaveLength(2);
    expect(callsTo(fetchMock, "/apply")).toHaveLength(0);
  });
});

describe("server-side setup drafts", () => {
  it("restores selections and mappings without restoring final confirmation or storing paths in the browser", async () => {
    const fetchMock = await mountPage(existing, undefined, {
      draft: {
        id: "draft-one",
        request: {
          operation: "import",
          sourcePath: "/source/resumed",
          targetPath: "/storage/resumed",
          originalDataPath: "C:/old",
        },
        rules: [{ sourcePrefix: "C:/Media", targetPrefix: "/media" }],
      },
    });

    expect(input("import")).toBeChecked();
    expect(input("source")).toHaveValue("/source/resumed");
    expect(element("editDestination")).toHaveAttribute("aria-expanded", "true");
    expect(input("target")).toHaveValue("/storage/resumed");
    expect(element("draftStatus")).toHaveTextContent("Draft restored");
    expect(element("step3")).toBeDisabled();
    expect(callsTo(fetchMock, "/preflight")).toHaveLength(0);
    expect(callsTo(fetchMock, "/apply")).toHaveLength(0);
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
    await toPreflight();
    await waitFor(() => expect(mappingInput("C:/Media")).toHaveValue("/media"));
    expect(
      callsTo(fetchMock, "/preflight").filter(([, options]) => options.method === "POST"),
    ).toHaveLength(1);
  });

  it("serializes autosaves so a slow old response cannot overwrite a newer draft", async () => {
    const pending = deferred<unknown>();
    const saved: unknown[] = [];

    await mountPage(firstRun, undefined, {
      saveDraft: (value) => {
        saved.push(value);

        return saved.length === 1 ? pending.promise : { draft: { id: "draft-one" } };
      },
    });
    selectSource("/source/first");
    await waitFor(() => expect(saved).toHaveLength(1));
    enter("source", "/source/latest");
    await new Promise((resolve) => setTimeout(resolve, 600));
    expect(saved).toHaveLength(1);
    pending.resolve({ draft: { id: "draft-one" } });
    await waitFor(() => expect(saved).toHaveLength(2));
    expect(saved[1]).toMatchObject({ request: { sourcePath: "/source/latest" } });
    await waitFor(() => expect(element("draftStatus")).toHaveTextContent("Draft saved"));
  });

  it("clears a draft only after in-flight saves finish and cancels queued stale saves", async () => {
    const pending = deferred<unknown>();
    const order: string[] = [];
    const fetchMock = await mountPage(firstRun, undefined, {
      saveDraft: () => {
        order.push("save");

        return pending.promise;
      },
      clearDraft: () => {
        order.push("clear");

        return { draft: null };
      },
    });

    selectSource("/source/old");
    await waitFor(() => expect(order).toEqual(["save"]));
    enter("source", "/source/new");
    click("clearDraft");
    await settle();
    expect(order).toEqual(["save"]);
    pending.resolve({ draft: { id: "draft-one" } });
    await waitFor(() => expect(order).toEqual(["save", "clear"]));
    expect(input("source")).toHaveValue("");
    expect(input("empty")).toBeChecked();
    expect(element("draftStatus")).toHaveTextContent("Saved draft cleared");
    await new Promise((resolve) => setTimeout(resolve, 600));
    expect(
      callsTo(fetchMock, "/draft").filter(([, options]) => options.method === "POST"),
    ).toHaveLength(1);
  });

  it("reports a failed autosave and lets the user retry without claiming data changed", async () => {
    let attempts = 0;

    await mountPage(firstRun, undefined, {
      saveDraft: () => {
        if (++attempts === 1) return { error: "Storage unavailable" };

        return { draft: { id: "draft-one" } };
      },
    });
    selectSource();
    await waitFor(() => expect(element("draftStatus")).toHaveTextContent("Storage unavailable"));
    expect(element("saveDraft")).toBeVisible();
    click("saveDraft");
    await waitFor(() => expect(element("draftStatus")).toHaveTextContent("Draft saved"));
    expect(element("saveDraft")).not.toBeVisible();
    expect(navigate).not.toHaveBeenCalled();
  });
});
