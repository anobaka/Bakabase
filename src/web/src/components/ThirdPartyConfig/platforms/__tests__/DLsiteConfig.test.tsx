import type { ReactNode } from "react";

import { createRoot, type Root } from "react-dom/client";
import { act } from "react-dom/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { DLsiteConfigField, DLsiteConfigPanel } from "../DLsiteConfig";

const { patchApi, storePatch, success } = vi.hoisted(() => ({
  patchApi: vi.fn(),
  storePatch: vi.fn(),
  success: vi.fn(),
}));

vi.mock("react-i18next", () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}));
vi.mock("@/sdk/BApi", () => ({
  default: { options: { patchDLsiteOptions: patchApi }, tool: {} },
}));
vi.mock("@/stores/options", () => {
  const state = {
    data: { accounts: [{ name: "Original", cookie: "old-cookie" }] },
    patch: storePatch,
  };

  return { useDLsiteOptionsStore: (selector: (state: typeof state) => unknown) => selector(state) };
});
vi.mock("@/stores/remoteAccess", () => ({ useCookieCaptureAvailable: () => false }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("@/components/FileSystemSelector", () => ({
  FileSystemSelectorButton: () => null,
  FileSystemSelectorModal: () => null,
}));
vi.mock("@/components/bakaui", () => ({
  toast: { success },
  NumberInput: () => null,
  Modal: () => null,
}));
vi.mock("@heroui/react", () => ({
  Button: ({ children, onPress }: { children: ReactNode; onPress?: () => void }) => (
    <button onClick={onPress}>{children}</button>
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
    <input
      aria-label={label}
      value={value}
      onChange={(event) => onValueChange(event.target.value)}
    />
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
    <textarea
      aria-label={label}
      value={value}
      onChange={(event) => onValueChange(event.target.value)}
    />
  ),
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  Divider: () => <hr />,
  Switch: () => null,
}));
vi.mock("../../base/MetadataMappingPanel", () => ({ default: () => null }));
vi.mock("../../base/AutoSyncPanel", () => ({ default: () => null }));
vi.mock("../../base/ProxyField", () => ({ default: () => null }));
vi.mock("../../base/ThirdPartyConfigModal", () => ({ default: () => null }));
vi.mock("@/pages/dlsite-works/components/LeStatusIndicator", () => ({
  LeStatusIndicator: () => null,
}));

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  vi.resetAllMocks();
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

describe("DLsiteConfigPanel accounts in downloader settings", () => {
  it.each([
    { code: 0, shouldToast: true },
    { code: 409, shouldToast: false },
  ])("saves edited accounts with response code $code", async ({ code, shouldToast }) => {
    patchApi.mockResolvedValue({ code });

    await act(async () =>
      root.render(<DLsiteConfigPanel fields={[DLsiteConfigField.Accounts]} showFooter={false} />),
    );

    const buttons = Array.from(container.querySelectorAll("button"));
    const save = buttons.find((button) => button.textContent === "thirdPartyConfig.action.save");

    expect(save).toBeDefined();
    expect(buttons.find((button) => button.textContent === "common.action.save")).toBeUndefined();

    const cookie = container.querySelector<HTMLTextAreaElement>(
      'textarea[aria-label="resourceSource.accounts.cookie"]',
    )!;

    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value")!.set!.call(
        cookie,
        "new-cookie",
      );
      cookie.dispatchEvent(new Event("input", { bubbles: true }));
    });
    await act(async () => save!.click());

    expect(patchApi).toHaveBeenCalledExactlyOnceWith({
      accounts: [{ name: "Original", cookie: "new-cookie" }],
    });
    expect(storePatch).not.toHaveBeenCalled();
    if (shouldToast) {
      expect(success).toHaveBeenCalledExactlyOnceWith("thirdPartyConfig.success.saved");
    } else {
      expect(success).not.toHaveBeenCalled();
    }
  });

  it("does not confirm a footer save when no accounts changed", async () => {
    await act(async () =>
      root.render(<DLsiteConfigPanel showFooter fields={[DLsiteConfigField.Accounts]} />),
    );

    const save = Array.from(container.querySelectorAll("button")).find(
      (button) => button.textContent === "common.action.save",
    );

    expect(save).toBeDefined();
    await act(async () => save!.click());

    expect(patchApi).not.toHaveBeenCalled();
    expect(storePatch).not.toHaveBeenCalled();
    expect(success).not.toHaveBeenCalled();
  });
});
