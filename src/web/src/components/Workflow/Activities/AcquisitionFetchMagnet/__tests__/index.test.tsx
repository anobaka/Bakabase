import type { ComponentProps, ReactNode } from "react";
import type { Root } from "react-dom/client";

import React, { useState } from "react";
import { createRoot } from "react-dom/client";
import { act } from "react-dom/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { AcquisitionFetchMagnetUI } from "..";
import { AcquisitionFetchTorrentUI } from "../../AcquisitionFetchTorrent";
import { AcquisitionFetchHttpUI } from "../../AcquisitionFetchHttp";

vi.mock("@/components/bakaui", () => ({
  Input: ({
    value,
    onValueChange,
    label,
  }: {
    value: string;
    onValueChange: (next: string) => void;
    label: string;
  }) => (
    <input
      aria-label={label}
      value={value}
      onChange={(event) => onValueChange(event.target.value)}
    />
  ),
  Select: ({
    dataSource,
    selectedKeys,
    onSelectionChange,
    description,
  }: {
    dataSource: { value: string; label: ReactNode }[];
    selectedKeys: string[];
    onSelectionChange: (keys: Set<string>) => void;
    description?: string;
  }) => (
    <>
      <select
        value={selectedKeys[0]}
        onChange={(event) => onSelectionChange(new Set([event.target.value]))}
      >
        {dataSource.map((item) => (
          <option key={item.value} value={item.value}>
            {item.label}
          </option>
        ))}
      </select>
      <p>{description}</p>
    </>
  ),
}));

type Config = ComponentProps<typeof AcquisitionFetchMagnetUI.ConfigForm>["value"];
const ConfigForm = AcquisitionFetchMagnetUI.ConfigForm;
const Form = ({ initial = AcquisitionFetchMagnetUI.defaultConfig() }: { initial?: Config }) => {
  const [value, setValue] = useState(initial);

  return (
    <>
      <ConfigForm value={value} onChange={setValue} />
      <output>{AcquisitionFetchMagnetUI.serializeConfig(value)}</output>
    </>
  );
};
let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
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
const select = async (value: string) => {
  await act(async () => {
    const input = container.querySelector("select")!;

    input.value = value;
    input.dispatchEvent(new Event("change", { bubbles: true }));
  });
};
const field = (key: string) =>
  container.querySelector<HTMLInputElement>(`input[aria-label="${key}"]`);
const config = () => JSON.parse(container.querySelector("output")!.textContent!);

describe("download action configuration", () => {
  it("defaults to the built-in magnet engine and only exposes RPC fields for aria2", async () => {
    await act(async () => root.render(<Form />));
    expect(container.querySelector("select")).toHaveValue("0");
    expect(field("workflow.acquisition.fetchMagnet.rpcUrl.label")).toBeNull();
    expect(field("workflow.acquisition.fetchMagnet.timeout.label")).toHaveValue("240");
    await select("1");
    expect(field("workflow.acquisition.fetchMagnet.rpcUrl.label")).toHaveValue(
      "http://127.0.0.1:6800/jsonrpc",
    );
    expect(field("workflow.acquisition.fetchMagnet.pollSeconds.label")).toHaveValue("5");
    await select("2");
    expect(field("workflow.acquisition.fetchMagnet.rpcUrl.label")).toBeNull();
    expect(field("workflow.acquisition.fetchMagnet.timeout.label")).toBeNull();
    expect(config().handler).toBe(2);
  });

  it("preserves explicit legacy aria2 configuration when changing the implementation", async () => {
    const initial = AcquisitionFetchMagnetUI.parseConfig(
      '{"handler":1,"rpcUrl":"http://server.invalid/rpc","secret":"saved-token","pollSeconds":9}',
    );

    await act(async () => root.render(<Form initial={initial} />));
    expect(container.querySelector("select")).toHaveValue("1");
    await select("0");
    await select("1");
    expect(config()).toMatchObject({
      handler: 1,
      rpcUrl: "http://server.invalid/rpc",
      secret: "saved-token",
      pollSeconds: 9,
    });
  });

  it.each([AcquisitionFetchTorrentUI, AcquisitionFetchHttpUI])(
    "keeps %s timeout defaults and rejects values outside the backend range",
    (ui) => {
      expect(ui.parseConfig("{}")).toEqual({ timeoutMinutes: 240 });
      expect(ui.isValid(ui.parseConfig('{"timeoutMinutes":60}'))).toBe(true);
      expect(ui.isValid(ui.parseConfig('{"timeoutMinutes":0}'))).toBe(false);
      expect(ui.isValid(ui.parseConfig('{"timeoutMinutes":43201}'))).toBe(false);
    },
  );
});
