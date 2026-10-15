import type { ResourceProfile } from "../sourceProfiles";

import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";

import ResourceScopeEditorModal from "../components/ResourceScopeEditorModal";

const api = vi.hoisted(() => ({
  add: vi.fn(),
  update: vi.fn(),
  properties: vi.fn(),
  resolve: vi.fn(),
  config: vi.fn(),
  saved: vi.fn(),
}));

vi.mock("../scopeApi", () => ({ resourceScopeApi: { add: api.add, update: api.update } }));
vi.mock("../sourceProfiles", () => ({
  profileSourceApi: { properties: api.properties, resolve: api.resolve },
}));
vi.mock("../sourceFilterConfig", () => ({ createSourceFilterConfig: api.config }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("@/components/ResourceFilter", () => ({
  ResourceFilterController: ({ group }: any) => <output>{JSON.stringify(group)}</output>,
}));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ children, visible, footer, onOk }: any) =>
    visible ? (
      <section>
        {children}
        <button disabled={footer.okProps.isDisabled} onClick={onOk}>
          Save
        </button>
      </section>
    ) : null,
  Input: ({ value, onValueChange, label, isDisabled }: any) => (
    <input
      aria-label={label}
      disabled={isDisabled}
      value={value}
      onChange={(e) => onValueChange(e.target.value)}
    />
  ),
}));
let host: HTMLDivElement;
let root: Root;
const profile = {
  id: 1,
  name: "Films",
  priority: 10,
  createdAt: "",
  updatedAt: "",
  search: { page: 1, pageSize: 100, group: { combinator: 1, disabled: false, filters: [] } },
} as ResourceProfile;

beforeEach(() => {
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  Object.values(api).forEach((mock) => mock.mockReset());
  api.add.mockResolvedValue({ id: 8, revision: 1 });
  api.update.mockResolvedValue({ revision: 2 });
  host = document.createElement("div");
  document.body.append(host);
  root = createRoot(host);
});
afterEach(async () => {
  await act(async () => root.unmount());
  host.remove();
  vi.unstubAllGlobals();
});
const render = async (props: any = {}) =>
  act(async () =>
    root.render(<ResourceScopeEditorModal profile={profile} onSaved={api.saved} {...props} />),
  );
const save = async () => act(async () => host.querySelector<HTMLButtonElement>("button")!.click());

it("retains a failed link and retries without creating duplicate conditions", async () => {
  api.saved.mockRejectedValueOnce(new Error("Profile changed"));
  await render();
  await save();
  expect(host.textContent).toContain("Profile changed");
  expect(api.add).toHaveBeenCalledTimes(1);
  await save();
  expect(api.add).toHaveBeenCalledTimes(1);
  expect(api.update).toHaveBeenCalledWith(
    expect.objectContaining({ id: 8, revision: 1, name: "Films" }),
  );
  expect(api.saved.mock.calls).toEqual([[8], [8]]);
  expect(host.textContent).toBe("");
});
it("preserves a shared edit and expected revision when another editor wins", async () => {
  api.update.mockRejectedValueOnce(new Error("Scope changed"));
  await render({
    scope: { id: 8, revision: 5, name: "Shared films", ruleCount: 2, search: profile.search },
  });
  await save();
  expect(host.querySelector("input")?.value).toBe("Shared films");
  expect(host.textContent).toContain("Scope changed");
  expect(api.saved).not.toHaveBeenCalled();
  expect(api.update).toHaveBeenCalledWith(expect.objectContaining({ id: 8, revision: 5 }));
});
it("fails closed for foreign conditions when their property catalog is offline", async () => {
  api.properties.mockRejectedValue(new Error("offline"));
  api.resolve.mockResolvedValue(profile.search);
  await render({ profile: { ...profile, sourceDeviceId: "other", sourceLibraryEpoch: "epoch" } });
  expect(host.querySelector("button")).toBeDisabled();
  expect(host.querySelector("output")).toBeNull();
  expect(api.add).not.toHaveBeenCalled();
  expect(api.config).not.toHaveBeenCalled();
});
