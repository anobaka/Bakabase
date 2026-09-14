import { createRoot } from "react-dom/client";
import { act } from "react-dom/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { ReferenceItemActions } from "../ReferenceItemTools";

const state = vi.hoisted(() => ({ createPortal: vi.fn(), danger: vi.fn() }));

vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: state.createPortal }),
}));
vi.mock("../../ReferenceValueUsage", () => ({ default: () => null }));
vi.mock("@/components/bakaui/components/ColorPicker", () => ({
  buildColorValueString: (color: string) => color,
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, "aria-label": label }: any) => (
    <button aria-label={label} onClick={onPress}>
      {children}
    </button>
  ),
  ColorPicker: () => null,
  Modal: () => null,
  toast: { danger: state.danger },
}));

let container: HTMLDivElement;
let root: ReturnType<typeof createRoot>;

beforeEach(() => {
  (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
  state.createPortal.mockReset();
  state.danger.mockReset();
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
});

async function renderAndDelete(
  checkUsage: (value: string) => Promise<number>,
  onRemove: () => void,
) {
  await act(async () =>
    root.render(
      <ReferenceItemActions
        checkUsage={checkUsage}
        value="saved-id"
        onRemove={onRemove}
        onToggleHidden={() => {}}
      />,
    ),
  );
  await act(async () =>
    container
      .querySelector<HTMLButtonElement>('[aria-label="property.referenceEditor.delete"]')!
      .click(),
  );
}

describe("reference option deletion", () => {
  it("keeps the option and reports an unavailable usage check", async () => {
    const remove = vi.fn();

    await renderAndDelete(async () => {
      throw new Error("Index warming");
    }, remove);
    expect(remove).not.toHaveBeenCalled();
    expect(state.createPortal).not.toHaveBeenCalled();
    expect(state.danger).toHaveBeenCalledWith("property.referenceEditor.usageCheckFailed");
  });

  it("waits for confirmation when the exact saved ID has references", async () => {
    const remove = vi.fn();
    const check = vi.fn().mockResolvedValue(3);

    await renderAndDelete(check, remove);
    expect(check).toHaveBeenCalledWith("saved-id");
    expect(remove).not.toHaveBeenCalled();
    await act(async () => state.createPortal.mock.calls[0][1].onOk());
    expect(remove).toHaveBeenCalledOnce();
  });

  it("removes an option directly after a successful zero-reference check", async () => {
    const remove = vi.fn();

    await renderAndDelete(async () => 0, remove);
    expect(remove).toHaveBeenCalledOnce();
    expect(state.createPortal).not.toHaveBeenCalled();
  });
});
