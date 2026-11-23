import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import DraftPreparation from "../DraftPreparation";
const mocks = vi.hoisted(() => ({
  builders: vi.fn(),
  updateDraft: vi.fn(),
  setBuilder: vi.fn(),
  search: vi.fn(),
  prepare: vi.fn(),
  portal: vi.fn(),
  preflight: vi.fn(),
}));
vi.mock("../api", () => ({ planTaskApi: mocks }));
vi.mock("../../api", () => ({ defaultAcquireAdapter: { preflight: mocks.preflight } }));
vi.mock("../../AcquireModal", () => ({ default: () => null }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: mocks.portal }),
}));
vi.mock("@/components/bakaui", () => ({
  Accordion: ({ children }: any) => <div>{children}</div>,
  AccordionItem: ({ children }: any) => <div>{children}</div>,
  Spinner: () => <span>loading</span>,
  Button: ({ children, onPress, isDisabled }: any) => (
    <button disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Input: ({ label, value, onValueChange, isDisabled }: any) => (
    <input
      aria-label={label}
      value={value}
      disabled={isDisabled}
      onChange={(e) => onValueChange(e.target.value)}
    />
  ),
  Select: ({ label, dataSource, selectedKeys, onSelectionChange }: any) => (
    <select
      aria-label={label}
      value={selectedKeys[0] ?? ""}
      onChange={(e) => onSelectionChange(new Set([e.target.value]))}
    >
      {dataSource.map((item: any) => (
        <option value={item.value} key={item.value}>
          {item.label}
        </option>
      ))}
    </select>
  ),
}));
const detail: any = {
  task: {
    id: 42,
    title: "Saved intent",
    builderId: "draft",
    planRevision: 0,
    targetDirectory: "/saved",
  },
  draftSources: [
    {
      id: "source",
      url: "https://example.com/file",
      kind: "auto",
      archivePassword: "saved-password",
      processing: { requirement: "unknown", steps: [], evidence: [] },
    },
  ],
};
const action = async (fn: () => Promise<unknown>) => {
  await fn();
};
beforeEach(() => {
  vi.clearAllMocks();
  mocks.builders.mockResolvedValue([
    {
      id: "search",
      nameKey: "search builder",
      descriptionKey: "description",
      entryPoints: ["manual"],
      inputKind: "title",
    },
  ]);
  mocks.updateDraft.mockResolvedValue({ ...detail, task: { ...detail.task, planRevision: 1 } });
  mocks.setBuilder.mockResolvedValue({ ...detail, task: { ...detail.task, planRevision: 2 } });
  mocks.search.mockResolvedValue(detail);
  mocks.prepare.mockResolvedValue({ ...detail, plan: { parts: [] } });
});
it("saves edits then searches using the same task and current revision, without creating another task", async () => {
  render(<DraftPreparation detail={{...detail, draftSources: []}} busy={false} onAction={action} />);
  await screen.findByRole("combobox");
  fireEvent.change(screen.getByLabelText("acquisition.plan.titleLabel"), {
    target: { value: "New query" },
  });
  fireEvent.click(screen.getByText("acquisition.draft.search"));
  await waitFor(() => expect(mocks.search).toHaveBeenCalledWith(42, "New query"));
  expect(mocks.updateDraft).toHaveBeenCalledWith(
    42,
    expect.objectContaining({ expectedRevision: 0, title: "New query" }),
  );
  expect(mocks.setBuilder).toHaveBeenCalledWith(42, 1, "search");
  expect(mocks.portal).not.toHaveBeenCalled();
});
it("hides title-search builders when download sources already exist", async () => {
  render(<DraftPreparation detail={detail} busy={false} onAction={action} />);
  await waitFor(() => expect(mocks.builders).toHaveBeenCalled());
  expect(screen.queryByRole("combobox")).toBeNull();
  expect(screen.queryByRole("button", { name: "acquisition.draft.search" })).toBeNull();
  expect(mocks.setBuilder).not.toHaveBeenCalled();
});
