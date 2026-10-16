import type { NavigationEditorProps } from "@/layouts/BasicLayout/components/PageNav/components/NavigationEditor";

import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";

import NavigationCustomization from ".";

const mocks = vi.hoisted(() => ({
  routesReady: false,
  catalogLoaded: false,
  patch: vi.fn().mockResolvedValue({ code: 0 }),
  update: vi.fn(),
}));

vi.mock("@/layouts/BasicLayout/components/PageNav/components/AntdMenu/menuConfig", () => {
  // The route catalog imports configuration. Reading it while the configuration
  // page is initializing reproduces the production bundle's temporal dead zone.
  if (!mocks.routesReady) throw new ReferenceError("Route catalog has not initialized");
  mocks.catalogLoaded = true;

  return { asideMenuConfig: [{ name: "menu.resource", path: "/resource" }] };
});
vi.mock("@/layouts/BasicLayout/components/PageNav/components/NavigationEditor", () => ({
  default: ({ catalog, onSave, onClose }: NavigationEditorProps) => (
    <section aria-label="Navigation editor">
      <span>{catalog[0]?.name}</span>
      <button
        onClick={() => onSave({ version: 1, items: [], knownCatalogIds: ["menu:menu.resource"] })}
      >
        Save
      </button>
      <button onClick={onClose}>Close</button>
    </section>
  ),
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress }: any) => <button onClick={onPress}>{children}</button>,
}));
vi.mock("@/sdk/BApi", () => ({ default: { options: { patchUiOptions: mocks.patch } } }));
vi.mock("@/stores/options", () => ({
  useUiOptionsStore: () => ({ data: {}, update: mocks.update }),
}));
vi.mock("@/stores/remoteAccess", () => ({
  useIsPureClient: () => false,
  useRemoteAccessStore: (selector: any) =>
    selector({ initialized: true, federationAvailable: true }),
}));

afterEach(cleanup);

it("loads the route catalog only when opening the editor after route initialization", async () => {
  render(<NavigationCustomization />);
  expect(mocks.catalogLoaded).toBe(false);

  mocks.routesReady = true;
  fireEvent.click(screen.getByRole("button", { name: "navigationEditor.open.label" }));
  expect(await screen.findByRole("region", { name: "Navigation editor" })).toBeTruthy();
  expect(screen.getByText("menu.resource")).toBeTruthy();
  expect(mocks.catalogLoaded).toBe(true);

  fireEvent.click(screen.getByRole("button", { name: "Save" }));
  await waitFor(() => expect(mocks.update).toHaveBeenCalledTimes(1));
  expect(mocks.patch).toHaveBeenCalledWith({
    navigationLayout: {
      version: 1,
      items: [],
      knownCatalogIds: ["menu:menu.resource"],
      directoryAliases: {},
    },
  });

  fireEvent.click(screen.getByRole("button", { name: "Close" }));
  expect(screen.queryByRole("region", { name: "Navigation editor" })).toBeNull();
});
