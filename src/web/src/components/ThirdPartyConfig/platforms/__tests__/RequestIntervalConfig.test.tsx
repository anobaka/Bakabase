import type { ReactNode } from "react";

import { createRoot, type Root } from "react-dom/client";
import { act } from "react-dom/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { BangumiConfigField, BangumiConfigPanel } from "../BangumiConfig";
import { ExHentaiConfigField, ExHentaiConfigPanel } from "../ExHentaiConfig";

const { options, patchExHentai, patchBangumi } = vi.hoisted(() => ({
  options: {
    exHentai: {} as { requestInterval?: number },
    bangumi: {} as { requestInterval?: number },
  },
  patchExHentai: vi.fn(),
  patchBangumi: vi.fn(),
}));

vi.mock("react-i18next", () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}));
vi.mock("@/sdk/BApi", () => ({
  default: { options: { patchExHentaiOptions: patchExHentai } },
}));
vi.mock("@/stores/options", () => ({
  useExHentaiOptionsStore: (selector: (state: unknown) => unknown) =>
    selector({ data: options.exHentai, patch: patchExHentai }),
  useBangumiOptionsStore: (selector: (state: unknown) => unknown) =>
    selector({ data: options.bangumi, patch: patchBangumi }),
}));
vi.mock("../../base/useAutoSaveToast", () => ({
  default: (patch: (value: unknown) => unknown) => patch,
}));
vi.mock("../../base/ConfigurableThirdPartyPanel", () => ({
  default: ({
    fields,
    tabs,
  }: {
    fields: string[] | "all";
    tabs: { field: string; key: string; content: ReactNode }[];
  }) => (
    <div>
      {tabs
        .filter((tab) => fields === "all" || fields.includes(tab.field))
        .map((tab) => (
          <div key={tab.key}>{tab.content}</div>
        ))}
    </div>
  ),
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
  CheckboxGroup: () => null,
  Textarea: () => null,
}));
vi.mock("@/components/bakaui", () => ({
  NumberInput: ({
    label,
    value,
    onValueChange,
  }: {
    label: string;
    value: number;
    onValueChange: (value: number) => void;
  }) => (
    <input
      aria-label={label}
      type="number"
      value={value}
      onChange={(event) => onValueChange(Number(event.target.value))}
    />
  ),
  Checkbox: () => null,
  Chip: () => null,
  toast: { success: vi.fn() },
}));
vi.mock("../../base/AccountsPanel", () => ({ default: () => null }));
vi.mock("../../base/MetadataMappingPanel", () => ({ default: () => null }));
vi.mock("../../base/AutoSyncPanel", () => ({ default: () => null }));
vi.mock("../../base/TampermonkeyInstallButton", () => ({ default: () => null }));
vi.mock("../../base/ThirdPartyConfigModal", () => ({ default: () => null }));
vi.mock("../../base/ProxyField", () => ({ default: () => null }));
vi.mock("../../base/DownloadResultWorkflowField", () => ({ default: () => null }));
vi.mock("@/components/FileSystemSelector", () => ({ FileSystemSelectorButton: () => null }));
vi.mock("@/pages/downloader/components/TaskDetailModal/components/PreferTorrentField", () => ({
  default: () => null,
}));

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  vi.resetAllMocks();
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  options.exHentai = {};
  options.bangumi = {};
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

const intervalInput = () =>
  container.querySelector<HTMLInputElement>(
    'input[aria-label="thirdPartyConfig.label.requestInterval"]',
  )!;

async function enterInterval(value: string) {
  const input = intervalInput();

  await act(async () => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}

describe("request interval configuration", () => {
  it("shows the saved 0 or 100 ms for ExHentai and defaults only when unset", async () => {
    options.exHentai = { requestInterval: 0 };
    await act(async () =>
      root.render(<ExHentaiConfigPanel fields={[ExHentaiConfigField.DataFetch]} />),
    );
    expect(intervalInput().value).toBe("0");

    options.exHentai = { requestInterval: 100 };
    await act(async () =>
      root.render(<ExHentaiConfigPanel fields={[ExHentaiConfigField.DataFetch]} />),
    );
    expect(intervalInput().value).toBe("100");
    await enterInterval("0");
    expect(patchExHentai).toHaveBeenCalledWith({ requestInterval: 0 });

    options.exHentai = {};
    await act(async () =>
      root.render(<ExHentaiConfigPanel fields={[ExHentaiConfigField.DataFetch]} />),
    );
    expect(intervalInput().value).toBe("1000");
  });

  it("keeps a typed 0 when saving Bangumi settings", async () => {
    await act(async () =>
      root.render(<BangumiConfigPanel fields={[BangumiConfigField.DataFetch]} />),
    );
    expect(intervalInput().value).toBe("1000");

    await enterInterval("0");
    expect(intervalInput().value).toBe("0");

    await act(async () => {
      container.querySelector<HTMLButtonElement>("button")!.click();
    });
    expect(patchBangumi).toHaveBeenCalledWith(expect.objectContaining({ requestInterval: 0 }));
  });
});
