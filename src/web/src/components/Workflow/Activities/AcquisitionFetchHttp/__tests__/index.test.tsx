import type { ComponentProps } from "react";
import type { Root } from "react-dom/client";

import React, { useState } from "react";
import { createRoot } from "react-dom/client";
import { act } from "react-dom/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { AcquisitionFetchHttpUI as ui } from "..";
import { AcquisitionFetchTorrentUI } from "../../AcquisitionFetchTorrent";

vi.mock("@/components/bakaui", () => ({
  Input: ({
    value,
    onValueChange,
    label,
    isInvalid,
    errorMessage,
  }: {
    value: string;
    onValueChange: (value: string) => void;
    label: string;
    isInvalid: boolean;
    errorMessage: string;
  }) => (
    <>
      <input
        aria-invalid={isInvalid}
        aria-label={label}
        value={value}
        onChange={(event) => onValueChange(event.target.value)}
      />
      {isInvalid && <span role="alert">{errorMessage}</span>}
    </>
  ),
}));

type Config = ComponentProps<typeof ui.ConfigForm>["value"];
const ConfigForm = ui.ConfigForm;
const Form = ({ initial }: { initial: Config }) => {
  const [value, setValue] = useState(initial);

  return (
    <>
      <ConfigForm value={value} onChange={setValue} />
      <output data-valid={ui.isValid(value)}>{ui.serializeConfig(value)}</output>
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
const field = (key: string) =>
  container.querySelector<HTMLInputElement>(
    `input[aria-label="workflow.acquisition.fetchHttp.${key}.label"]`,
  )!;
const change = async (key: string, value: string) => {
  await act(async () => {
    const input = field(key);

    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
};

describe("HTTP download configuration", () => {
  it("adds safe defaults to legacy HTTP JSON without adding fields to torrent configuration", () => {
    expect(ui.parseConfig('{"timeoutMinutes":60}')).toEqual({
      timeoutMinutes: 60,
      parallelConnections: 4,
      maxRetries: 3,
      speedLimitKiB: 0,
    });
    expect(ui.parseConfig("not json")).toEqual(ui.defaultConfig());
    expect(AcquisitionFetchTorrentUI.parseConfig("{}")).toEqual({ timeoutMinutes: 240 });
  });

  it("edits each setting independently and preserves all fields in the saved configuration", async () => {
    await act(async () => root.render(<Form initial={ui.parseConfig('{"timeoutMinutes":60}')} />));
    expect(container.querySelectorAll("input")).toHaveLength(4);
    await change("parallelConnections", "8");
    await change("maxRetries", "5");
    await change("speedLimitKiB", "2048");
    await change("timeoutMinutes", "120");
    expect(JSON.parse(container.querySelector("output")!.textContent!)).toEqual({
      timeoutMinutes: 120,
      parallelConnections: 8,
      maxRetries: 5,
      speedLimitKiB: 2048,
    });
    expect(container.querySelector("output")).toHaveAttribute("data-valid", "true");
    expect(container.textContent).toContain("workflow.acquisition.fetchHttp.automaticTransferHelp");
  });

  it("allows clearing and correcting a field without silently replacing it with its minimum", async () => {
    await act(async () => root.render(<Form initial={ui.defaultConfig()} />));
    await change("parallelConnections", "");
    expect(field("parallelConnections")).toHaveValue("");
    expect(field("parallelConnections")).toHaveAttribute("aria-invalid", "true");
    expect(container.querySelector("output")).toHaveAttribute("data-valid", "false");
    await change("parallelConnections", "16");
    await change("maxRetries", "0");
    await change("speedLimitKiB", "0");
    expect(container.querySelector("output")).toHaveAttribute("data-valid", "true");
    expect(field("maxRetries")).toHaveValue("0");
  });

  it.each([
    ["parallelConnections", 0],
    ["parallelConnections", 17],
    ["parallelConnections", 1.5],
    ["maxRetries", -1],
    ["maxRetries", 11],
    ["maxRetries", 0.5],
    ["speedLimitKiB", -1],
    ["speedLimitKiB", 1048577],
    ["timeoutMinutes", 0],
    ["timeoutMinutes", 43201],
  ])("rejects invalid %s = %s from saved JSON", (key, value) => {
    expect(ui.isValid(ui.parseConfig(JSON.stringify({ [key]: value })))).toBe(false);
  });
});
