import type { ReactNode } from "react";

import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, useLocation, useNavigate } from "react-router-dom";
import { afterEach, beforeEach, expect, it, vi } from "vitest";

import AppRouter from "@/router";
import { ClientMode } from "@/sdk/constants";

const mocks = vi.hoisted(() => ({
  context: vi.fn(),
  options: vi.fn(),
  app: vi.fn(),
  provider: vi.fn(),
  page: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    remoteAccess: { getRemoteAccessContext: mocks.context },
    options: { getUiOptions: mocks.options, getAppOptions: mocks.app },
  },
}));
vi.mock("@/components/routesMenuConfig", () => ({
  routesMenuConfig: [
    {
      name: "resource",
      path: "/resource",
      component: () => {
        mocks.page();

        return <p>Resource page</p>;
      },
    },
    { name: "home", path: "/", component: () => <p>Home page</p> },
    { name: "previous", path: "/previous", component: () => <p>Previous page</p> },
  ],
}));
vi.mock("@/layouts/BasicLayout/components/PageNav/components/AntdMenu/menuConfig", () => ({
  asideMenuConfig: [
    { name: "resource", path: "/resource" },
    { name: "menu.tools", children: [{ name: "workflow", path: "/workflow" }] },
  ],
}));
vi.mock("@/i18n", () => ({
  default: {
    language: "en",
    getFixedT: (language: string) => (key: string) =>
      language === "cn" && key === "menu.tools" ? "工具" : key,
  },
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  default: ({ children }: { children: ReactNode }) => {
    mocks.provider();

    return <>{children}</>;
  },
}));
vi.mock("@/layouts/BasicLayout", () => ({
  default: ({ children }: { children: ReactNode }) => <>{children}</>,
}));
vi.mock("@/layouts/BlankLayout", () => ({
  default: ({ children }: { children: ReactNode }) => <>{children}</>,
}));
vi.mock("@/components/utils", () => ({ buildLogger: () => () => {} }));
vi.mock("@/services/Analytics", () => ({ trackPageView: vi.fn() }));

const context = {
  code: 0,
  data: {
    isLocal: true,
    clientMode: ClientMode.AllInOne,
    serverReachable: true,
    federationAvailable: true,
  },
};
const options = { code: 0, data: {} };
const deferred = <T,>() => {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => {
    resolve = done;
  });

  return { promise, resolve };
};

function LocationProbe() {
  const location = useLocation();
  const navigate = useNavigate();

  return (
    <>
      <span data-testid="location">
        {location.pathname}
        {location.search}
      </span>
      <button onClick={() => navigate(-1)}>Back</button>
    </>
  );
}
const open = (route = "/device-switch?path=%2Fresource") =>
  render(
    <MemoryRouter initialEntries={["/previous", route]} initialIndex={1}>
      <AppRouter />
      <LocationProbe />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  mocks.context.mockResolvedValue(context);
  mocks.options.mockResolvedValue(options);
  mocks.app.mockResolvedValue({ code: 0, data: { language: "en" } });
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

it.each(["/device-switch", "/device-switch/", "/DEVICE-SWITCH"])(
  "waits for destination context and layout without mounting the provider or resource page (%s)",
  async (route) => {
    const pendingContext = deferred<typeof context>();
    const pendingOptions = deferred<typeof options>();

    mocks.context.mockReturnValue(pendingContext.promise);
    mocks.options.mockReturnValue(pendingOptions.promise);
    open(`${route}?path=%2Fresource%3Fowner%3Dsource%26resourceId%3D12`);
    await screen.findByRole("status");
    expect(mocks.provider).not.toHaveBeenCalled();
    expect(mocks.page).not.toHaveBeenCalled();
    await act(async () => {
      pendingContext.resolve(context);
    });
    expect(mocks.provider).not.toHaveBeenCalled();
    await act(async () => {
      pendingOptions.resolve(options);
    });
    expect(await screen.findByText("Resource page")).toBeTruthy();
    expect(screen.getByTestId("location")).toHaveTextContent(/^\/resource$/);
    expect(mocks.context).toHaveBeenCalledTimes(1);
    expect(mocks.options).toHaveBeenCalledTimes(1);
    expect(mocks.app).toHaveBeenCalledTimes(1);
    expect(mocks.context.mock.calls[0][0].signal).toBeInstanceOf(AbortSignal);
    fireEvent.click(screen.getByRole("button", { name: "Back" }));
    expect(await screen.findByText("Previous page")).toBeTruthy();
    expect(mocks.context).toHaveBeenCalledTimes(1);
  },
);

it.each([
  ["failed options", context, { code: 500 }],
  ["missing context", { code: 0 }, options],
  ["unknown capability", { code: 0, data: { isLocal: true, clientMode: 999 } }, options],
  ["unreachable server", { code: 0, data: { ...context.data, serverReachable: false } }, options],
])(
  "keeps a recoverable error instead of guessing home for %s",
  async (_, contextReply, optionsReply) => {
    mocks.context.mockResolvedValueOnce(contextReply);
    mocks.options.mockResolvedValueOnce(optionsReply);
    open();
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "federation.switcher.destinationFailed",
    );
    expect(mocks.provider).not.toHaveBeenCalled();
    expect(screen.getByTestId("location")).toHaveTextContent("/device-switch");
    fireEvent.click(screen.getByRole("button", { name: "federation.retry" }));
    expect(await screen.findByText("Resource page")).toBeTruthy();
    expect(mocks.context).toHaveBeenCalledTimes(2);
    expect(mocks.options).toHaveBeenCalledTimes(2);
  },
);

it("bounds a hanging request, aborts both calls and lets the user choose home", async () => {
  mocks.context.mockReturnValue(new Promise(() => {}));
  mocks.options.mockReturnValue(new Promise(() => {}));
  vi.useFakeTimers();
  open();
  await act(async () => {
    await vi.advanceTimersByTimeAsync(0);
  });
  expect(screen.getByRole("status")).toBeTruthy();
  await act(async () => {
    await vi.advanceTimersByTimeAsync(10_000);
  });
  expect(screen.getByRole("alert")).toBeTruthy();
  expect(mocks.context.mock.calls[0][0].signal.aborted).toBe(true);
  expect(mocks.options.mock.calls[0][0].signal.aborted).toBe(true);
  expect(mocks.app.mock.calls[0][0].signal.aborted).toBe(true);
  expect(mocks.provider).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "federation.switcher.goHome" }));
  expect(screen.getByText("Home page")).toBeTruthy();
});

it("aborts pending requests when the landing unmounts", async () => {
  mocks.context.mockReturnValue(new Promise(() => {}));
  mocks.options.mockReturnValue(new Promise(() => {}));
  const view = open();

  await screen.findByRole("status");
  view.unmount();
  expect(mocks.context.mock.calls[0][0].signal.aborted).toBe(true);
  expect(mocks.options.mock.calls[0][0].signal.aborted).toBe(true);
});

it("uses home for a menu genuinely absent from the destination", async () => {
  open("/device-switch?path=%2Fnot-in-this-version");
  await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent(/^\/$/));
  expect(mocks.context).toHaveBeenCalledTimes(1);
  expect(mocks.options).toHaveBeenCalledTimes(1);
});

it("uses the destination app language when a hidden named group absorbs new menus", async () => {
  mocks.app.mockResolvedValue({ code: 0, data: { language: "cn" } });
  mocks.options.mockResolvedValue({
    code: 0,
    data: {
      navigationLayout: {
        version: 1,
        items: [
          { id: "custom:tools", kind: "directory", name: "工具", hidden: true, children: [] },
        ],
        knownCatalogIds: [],
      },
    },
  });
  open("/device-switch?path=%2Fworkflow");
  expect(await screen.findByText("Home page")).toBeTruthy();
  expect(screen.queryByRole("alert")).toBeNull();
});

it("shows retry when the destination app language cannot be read", async () => {
  mocks.app.mockResolvedValueOnce({ code: 500 });
  open();
  expect(await screen.findByRole("alert")).toBeTruthy();
  expect(mocks.provider).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "federation.retry" }));
  expect(await screen.findByText("Resource page")).toBeTruthy();
});
