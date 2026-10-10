import type { ReactNode } from "react";

import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import AliasPage from "..";
import AliasTextModal from "../components/AliasTextModal";

import enAlias from "@/locales/en/pages/alias.json";

const { api, portal, destroy, openUrl } = vi.hoisted(() => ({
  api: {
    searchAliasGroups: vi.fn(),
    addAlias: vi.fn(),
    patchAlias: vi.fn(),
    deleteAlias: vi.fn(),
    mergeAliasGroups: vi.fn(),
    deleteAliasGroups: vi.fn(),
    importAliases: vi.fn(),
  },
  portal: vi.fn(),
  destroy: vi.fn(),
  openUrl: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({ default: { alias: api } }));
vi.mock("@/config/env", () => ({ toAbsoluteBackendUrl: (path: string) => `http://server${path}` }));
vi.mock("@/utils/openExternalUrl", () => ({ openExternalUrl: openUrl }));
vi.mock("@/components/FileSystemSelector", () => ({ FileSystemSelectorModal: () => null }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: portal }),
}));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, values: Record<string, string | number> = {}) => {
      const messages: Record<string, string> = {
        ...enAlias,
        "common.action.delete": "Delete",
        "common.action.save": "Save",
      };
      return (messages[key] ?? key).replace(/\{\{(\w+)\}\}/g, (_, name) =>
        String(values[name] ?? ""),
      );
    },
  }),
}));
vi.mock("@/components/bakaui", async () => {
  const { cloneElement } = await import("react");
  return {
    Button: ({
      children,
      onPress,
      onClick,
      isDisabled,
      isLoading,
      type = "button",
      "aria-label": label,
    }: any) => (
      <button
        aria-label={label}
        disabled={isDisabled || isLoading}
        type={type}
        onClick={onPress ?? onClick}
      >
        {children}
      </button>
    ),
    Checkbox: ({ children, isSelected, isDisabled, onValueChange, "aria-label": label }: any) => (
      <label>
        <input
          aria-label={label}
          checked={isSelected}
          disabled={isDisabled}
          type="checkbox"
          onChange={(event) => onValueChange(event.target.checked)}
        />
        {children}
      </label>
    ),
    Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
    Input: ({ value = "", onValueChange, onClear, placeholder, "aria-label": label }: any) => (
      <div>
        <input
          aria-label={label}
          placeholder={placeholder}
          value={value}
          onChange={(event) => onValueChange(event.target.value)}
        />
        {onClear && value && (
          <button aria-label="Clear input" type="button" onClick={onClear}>
            Clear
          </button>
        )}
      </div>
    ),
    Popover: ({ trigger, visible, onVisibleChange, children }: any) => (
      <>
        {cloneElement(trigger, { onPress: () => onVisibleChange(!visible) })}
        {visible && <div role="menu">{children}</div>}
      </>
    ),
    Pagination: ({ page, total, onChange, isDisabled }: any) => (
      <nav aria-label="Pages">
        {Array.from({ length: total }, (_, index) => (
          <button
            key={index}
            aria-current={page === index + 1 ? "page" : undefined}
            disabled={isDisabled}
            onClick={() => onChange(index + 1)}
          >
            Page {index + 1}
          </button>
        ))}
      </nav>
    ),
    Spinner: () => <span>Spinner</span>,
    Modal: ({ children, onOk, footer, title }: any) => (
      <div role="dialog" aria-label={title}>
        {children}
        <button disabled={footer?.okProps?.isDisabled} onClick={onOk}>
          Save
        </button>
      </div>
    ),
  };
});

const alpha = { text: "Alpha", candidates: ["A", "Alternate"] };
const beta = { text: "Beta", candidates: [] };
const response = (data = [alpha, beta], totalCount = data.length) => ({
  code: 0,
  data,
  totalCount,
});

beforeEach(() => {
  vi.clearAllMocks();
  api.searchAliasGroups.mockReset().mockResolvedValue(response());
  for (const [name, method] of Object.entries(api))
    if (name !== "searchAliasGroups") method.mockResolvedValue({ code: 0 });
  portal.mockReturnValue({ destroy });
});
afterEach(cleanup);

async function open() {
  render(<AliasPage />);
  await screen.findByRole("heading", { name: "Alpha" });
}

describe("alias group management", () => {
  it("uses one-based server pagination and starts a trimmed search from page one", async () => {
    api.searchAliasGroups
      .mockResolvedValueOnce(response([alpha], 40))
      .mockResolvedValueOnce(response([beta], 40));
    await open();
    expect(api.searchAliasGroups).toHaveBeenCalledWith({ pageSize: 20, pageIndex: 1 });
    fireEvent.click(screen.getByRole("button", { name: "Page 2" }));
    await screen.findByRole("heading", { name: "Beta" });
    expect(api.searchAliasGroups).toHaveBeenLastCalledWith({ pageSize: 20, pageIndex: 2 });
    fireEvent.change(screen.getByRole("textbox", { name: "Search alias groups" }), {
      target: { value: "  Alternate  " },
    });
    fireEvent.click(screen.getByRole("button", { name: "Search" }));
    await waitFor(() =>
      expect(api.searchAliasGroups).toHaveBeenLastCalledWith({
        pageSize: 20,
        pageIndex: 1,
        fuzzyText: "Alternate",
      }),
    );
  });

  it("ignores an older search response after the user clears the search", async () => {
    let finish!: (value: ReturnType<typeof response>) => void;
    await open();
    api.searchAliasGroups
      .mockReturnValueOnce(
        new Promise((resolve) => {
          finish = resolve;
        }),
      )
      .mockResolvedValueOnce(response([beta]));
    fireEvent.change(screen.getByRole("textbox", { name: "Search alias groups" }), {
      target: { value: "Alpha" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Search" }));
    fireEvent.click(screen.getByRole("button", { name: "Clear input" }));
    await screen.findByRole("heading", { name: "Beta" });
    await act(async () => finish(response([alpha])));
    expect(screen.queryByRole("heading", { name: "Alpha" })).toBeNull();
    expect(screen.getByRole("heading", { name: "Beta" })).toBeInTheDocument();
  });

  it("keeps cross-page selection and the user's merge target order until confirmation", async () => {
    api.searchAliasGroups
      .mockResolvedValueOnce(response([alpha], 40))
      .mockResolvedValueOnce(response([beta], 40));
    await open();
    fireEvent.click(screen.getByRole("checkbox", { name: "Select group Alpha" }));
    fireEvent.click(screen.getByRole("button", { name: "Page 2" }));
    await screen.findByRole("heading", { name: "Beta" });
    fireEvent.click(screen.getByRole("checkbox", { name: "Select this page" }));
    fireEvent.click(
      screen.getByRole("button", { name: "Use Beta as the merged group's preferred name" }),
    );
    fireEvent.click(screen.getByRole("button", { name: "Merge" }));
    expect(api.mergeAliasGroups).not.toHaveBeenCalled();
    await act(async () => portal.mock.calls.at(-1)![1].onOk());
    expect(api.mergeAliasGroups).toHaveBeenCalledWith({ preferredTexts: ["Beta", "Alpha"] });
    expect(screen.queryByRole("button", { name: "Clear selection" })).toBeNull();
  });

  it("remaps a selected group after changing its preferred candidate", async () => {
    await open();
    fireEvent.click(screen.getByRole("checkbox", { name: "Select group Alpha" }));
    api.searchAliasGroups.mockResolvedValueOnce(
      response([{ text: "A", candidates: ["Alpha", "Alternate"] }, beta]),
    );
    fireEvent.click(screen.getByRole("button", { name: "Manage alias A" }));
    fireEvent.click(screen.getByRole("button", { name: "Set as preferred" }));
    await screen.findByRole("heading", { name: "A" });
    expect(api.patchAlias).toHaveBeenCalledWith({ isPreferred: true }, { text: "A" });
    expect(screen.getByRole("checkbox", { name: "Select group A" })).toBeChecked();
    const selection = screen.getByRole("region", { name: "Bulk operations" });
    fireEvent.click(within(selection).getByRole("button", { name: "Delete" }));
    await act(async () => portal.mock.calls.at(-1)![1].onOk());
    expect(api.deleteAliasGroups).toHaveBeenCalledWith({ preferredTexts: ["A"] });
  });

  it("offers candidate rename and group-local creation using the existing APIs", async () => {
    await open();
    fireEvent.click(screen.getByRole("button", { name: "Manage alias Alternate" }));
    fireEvent.click(screen.getByRole("button", { name: "Rename alias" }));
    const rename = portal.mock.calls.at(-1)![1];
    expect(rename.initialValue).toBe("Alternate");
    await act(async () => rename.onSubmit("Another"));
    expect(api.patchAlias).toHaveBeenCalledWith(
      { text: "Another", isPreferred: false },
      { text: "Alternate" },
    );
    fireEvent.click(screen.getAllByRole("button", { name: "Add alias" })[0]);
    await act(async () => portal.mock.calls.at(-1)![1].onSubmit("翻译"));
    expect(api.addAlias).toHaveBeenCalledWith({ text: "翻译", preferred: "Alpha" });
  });

  it("does not clear selection or claim success when deletion is rejected", async () => {
    await open();
    fireEvent.click(screen.getByRole("checkbox", { name: "Select group Alpha" }));
    fireEvent.click(screen.getByRole("button", { name: "Delete" }));
    api.deleteAliasGroups.mockResolvedValueOnce({ code: 1, message: "Rejected" });
    await expect(portal.mock.calls.at(-1)![1].onOk()).rejects.toThrow("Rejected");
    expect(screen.getByRole("checkbox", { name: "Select group Alpha" })).toBeChecked();
  });

  it("closes failed import progress and refreshes after a successful import", async () => {
    await open();
    fireEvent.click(screen.getByRole("button", { name: "Import" }));
    const importer = portal.mock.calls.at(-1)![1];
    expect(importer.filter({ path: "/tmp/ALIASES.CSV" })).toBe(true);
    api.importAliases.mockRejectedValueOnce(new Error("Invalid CSV"));
    await act(async () => importer.onSelected({ path: "/tmp/aliases.csv" }));
    expect(screen.getByRole("alert")).toHaveTextContent("Invalid CSV");
    expect(destroy).toHaveBeenCalledTimes(1);
    await act(async () => importer.onSelected({ path: "/tmp/aliases.csv" }));
    expect(destroy).toHaveBeenCalledTimes(2);
    expect(screen.queryByRole("alert")).toBeNull();
    expect(api.searchAliasGroups).toHaveBeenLastCalledWith({ pageSize: 20, pageIndex: 1 });
  });

  it("shows actionable empty/error states and exports through the existing client-aware helper", async () => {
    api.searchAliasGroups
      .mockRejectedValueOnce(new Error("Offline"))
      .mockResolvedValueOnce(response([]));
    render(<AliasPage />);
    expect(await screen.findByRole("alert")).toHaveTextContent("Offline");
    expect(screen.queryByRole("heading", { name: "Keep alternate names together" })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Retry" }));
    await screen.findByRole("heading", { name: "Keep alternate names together" });
    fireEvent.click(screen.getByRole("button", { name: "Export" }));
    expect(openUrl).toHaveBeenCalledWith("http://server/alias/xlsx");
  });

  it("returns to the last available page after the last group on a page is deleted", async () => {
    api.searchAliasGroups
      .mockResolvedValueOnce(response([alpha], 40))
      .mockResolvedValueOnce(response([beta], 40))
      .mockResolvedValueOnce(response([], 20))
      .mockResolvedValueOnce(response([alpha], 20));
    await open();
    fireEvent.click(screen.getByRole("button", { name: "Page 2" }));
    await screen.findByRole("heading", { name: "Beta" });
    fireEvent.click(screen.getByRole("checkbox", { name: "Select group Beta" }));
    fireEvent.click(screen.getByRole("button", { name: "Delete" }));
    await act(async () => portal.mock.calls.at(-1)![1].onOk());
    expect(api.searchAliasGroups).toHaveBeenLastCalledWith({ pageSize: 20, pageIndex: 1 });
    expect(screen.getByRole("heading", { name: "Alpha" })).toBeInTheDocument();
  });
});

describe("alias text editing", () => {
  it("rejects blank names and trims a name before sending it", () => {
    const submit = vi.fn();
    render(<AliasTextModal title="Name" onSubmit={submit} />);
    expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
    fireEvent.change(screen.getByRole("textbox"), { target: { value: "   " } });
    expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
    fireEvent.change(screen.getByRole("textbox"), { target: { value: "  新名称  " } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(submit).toHaveBeenCalledWith("新名称");
  });
});
