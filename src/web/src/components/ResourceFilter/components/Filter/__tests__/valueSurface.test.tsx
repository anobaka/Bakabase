import type { ReactNode } from "react";

import { execFileSync } from "node:child_process";
import { resolve } from "node:path";

import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { HeroUIProvider } from "@heroui/react";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

import SelectableChip from "@/components/StandardValue/ValueRenderer/Renderers/components/SelectableChip";
import { Input } from "@/components/bakaui/components/Input";
import DateInput from "@/components/bakaui/components/Date/DateInput";

vi.mock("@/components/bakaui", async () => ({
  Chip: (await import("@/components/bakaui/components/Chip")).default,
}));

let host: HTMLDivElement;
let root: Root;
let stylesheet: HTMLStyleElement;
let surfaceClassName: string;
let compiledCss: string;

// Vitest intentionally proxies CSS imports. Compile this one real stylesheet with
// Vite's Sass/CSS-module pipeline so the assertions can detect selectors that match no DOM.
beforeAll(() => {
  const filename = resolve("src/components/ResourceFilter/components/Filter/value.module.scss");
  // esbuild requires Node's typed-array realm, so keep preprocessing outside jsdom.
  const output = JSON.parse(
    execFileSync(
      process.execPath,
      [
        "--input-type=module",
        "-e",
        `
    import { readFileSync } from "node:fs";
    import { preprocessCSS, resolveConfig } from "vite";
    const filename = process.argv[1];
    const config = await resolveConfig({ configFile: false, logLevel: "silent" }, "serve");
    const result = await preprocessCSS(readFileSync(filename, "utf8"), filename, config);
    process.stdout.write(JSON.stringify({ code: result.code, modules: result.modules }));
  `,
        filename,
      ],
      { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] },
    ),
  );

  surfaceClassName = output.modules.value;
  compiledCss = output.code;
});
async function render(content: ReactNode) {
  await act(async () => root.render(<HeroUIProvider disableAnimation>{content}</HeroUIProvider>));
}
beforeEach(() => {
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  stylesheet = document.createElement("style");
  stylesheet.textContent = compiledCss;
  document.head.appendChild(stylesheet);
  host = document.createElement("div");
  document.body.appendChild(host);
  root = createRoot(host);
});
afterEach(async () => {
  await act(async () => root.unmount());
  host.remove();
  stylesheet.remove();
  vi.unstubAllGlobals();
});

describe("filter value surfaces with real HeroUI DOM", () => {
  it("actually applies the filter surface to chips without changing colors or chips elsewhere", async () => {
    const onClick = vi.fn();
    const label = "A long source or tag label that needs to wrap inside a narrow condition";

    await render(
      <>
        <div data-filter className={surfaceClassName}>
          <SelectableChip
            isSelected
            color="#3579ab"
            itemKey="inside"
            label={label}
            size="sm"
            onClick={onClick}
          />
        </div>
        <SelectableChip
          isSelected
          color="#3579ab"
          itemKey="outside"
          label="Outside filters"
          size="sm"
        />
      </>,
    );
    const chips = host.querySelectorAll<HTMLElement>("[data-value-option]");

    expect(chips).toHaveLength(2);
    const inside = chips[0];
    const outside = chips[1];
    const content = inside.querySelector<HTMLElement>(".standard-value-option-content")!;

    expect(getComputedStyle(inside).minHeight).toBe("2rem");
    expect(getComputedStyle(inside).height).toBe("auto");
    expect(getComputedStyle(inside).borderTopWidth).toBe("0px");
    expect(getComputedStyle(content).whiteSpace).toBe("normal");
    expect(content).toHaveTextContent(label);
    expect(getComputedStyle(outside).minHeight).not.toBe("2rem");
    expect(inside.style.color).toBe(outside.style.color);
    expect(inside.style.backgroundColor).toBe(outside.style.backgroundColor);
    await act(async () => {
      inside.click();
    });
    expect(onClick).toHaveBeenCalledOnce();
  });

  it("keeps disabled options unavailable while the filter's border and target-size styling applies", async () => {
    const onClick = vi.fn();

    await render(
      <div className={surfaceClassName}>
        <SelectableChip
          isDisabled
          color="#3579ab"
          isSelected={false}
          itemKey="disabled"
          label="Unavailable"
          size="sm"
          onClick={onClick}
        />
      </div>,
    );
    const option = host.querySelector<HTMLElement>("[data-value-option]")!;

    expect(getComputedStyle(option).minHeight).toBe("2rem");
    expect(getComputedStyle(option).borderTopWidth).toBe("0px");
    expect(option.style.color).toBe("rgb(53, 121, 171)");
    await act(async () => {
      option.click();
    });
    expect(onClick).not.toHaveBeenCalled();
  });

  it("matches the actual shared text and date input wrappers without altering their values", async () => {
    await render(
      <div className={surfaceClassName}>
        <Input aria-label="Numeric condition" size="sm" value="0" />
        <DateInput aria-label="Date condition" size="sm" />
      </div>,
    );
    const wrappers = host.querySelectorAll<HTMLElement>('[data-slot="input-wrapper"]');

    expect(wrappers).toHaveLength(2);
    wrappers.forEach((wrapper) => {
      expect(getComputedStyle(wrapper).minHeight).toBe("2rem");
      expect(getComputedStyle(wrapper).boxShadow).toBe("none");
    });
    expect(host.querySelector<HTMLInputElement>('[aria-label="Numeric condition"]')).toHaveValue(
      "0",
    );
  });
});
