import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import TaskStoragePolicySettings from "../TaskStoragePolicySettings";
import { defaultStoragePolicy } from "../../StoragePolicyFields";

vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, isDisabled, isLoading }: any) => (
    <button disabled={isDisabled || isLoading} onClick={onPress}>
      {children}
    </button>
  ),
  Chip: ({ children }: any) => <span>{children}</span>,
  Accordion: ({ children }: any) => <div>{children}</div>,
  AccordionItem: ({ children, title }: any) => <section aria-label={title}>{children}</section>,
  Input: ({ label, value, onValueChange, isDisabled }: any) => (
    <label>
      {label}
      <input value={value} disabled={isDisabled} onChange={(e) => onValueChange(e.target.value)} />
    </label>
  ),
  Checkbox: ({ children, isSelected, isDisabled, onValueChange }: any) => (
    <label>
      <input
        type="checkbox"
        checked={isSelected}
        disabled={isDisabled}
        onChange={(e) => onValueChange(e.target.checked)}
      />
      {children}
    </label>
  ),
  Select: ({ children, dataSource, label, selectedKeys, onSelectionChange, isDisabled }: any) => (
    <label>
      {label}
      <select
        value={[...selectedKeys][0]}
        disabled={isDisabled}
        onChange={(e) => onSelectionChange(new Set([e.target.value]))}
      >
        {children ??
          dataSource?.map((item: any) => (
            <option key={item.value} value={item.value}>
              {item.label}
            </option>
          ))}
      </select>
    </label>
  ),
  SelectItem: ({ children, textValue }: any) => (
    <option value={String(textValue).split(".").at(-1)}>{children}</option>
  ),
}));
vi.mock("@/components/FileSystemSelector/components/Button", () => ({
  default: ({ children }: any) => <button>{children}</button>,
}));
const detail = (changes: any = {}): any => ({
  task: { id: 2, planRevision: 4, isDeleted: false },
  canEditStoragePolicy: true,
  storagePolicy: defaultStoragePolicy,
  plan: {
    parts: [
      {
        id: "main",
        title: "Main",
        selectedSourceId: "s1",
        sources: [
          {
            id: "s1",
            processing: { steps: [{ id: "extract", op: "extract", input: "download" }] },
          },
        ],
        storagePolicy: { sourceCleanup: "quarantine" },
      },
    ],
  },
  ...changes,
});
afterEach(cleanup);
it("uses the selected source plan and submits only actual node differences with a fixed revision", async () => {
  const save = vi.fn().mockResolvedValue(true);
  render(<TaskStoragePolicySettings detail={detail()} busy={false} onSave={save} />);
  fireEvent.click(screen.getByRole("button", { name: "acquisition.storage.edit" }));
  const cleanupControls = screen.getAllByRole("checkbox", {
    name: "acquisition.storage.cleanupIntermediates",
  });
  expect(cleanupControls).toHaveLength(3);
  fireEvent.click(cleanupControls[2]);
  fireEvent.click(screen.getByRole("button", { name: "acquisition.storage.save" }));
  await waitFor(() =>
    expect(save).toHaveBeenCalledExactlyOnceWith(4, defaultStoragePolicy, [
      { partId: "main", policy: { sourceCleanup: "quarantine" } },
      { partId: "main", stepId: "extract", policy: { cleanupIntermediates: false } },
    ]),
  );
  await screen.findByRole("button", { name: "acquisition.storage.edit" });
});
it("keeps drafts but blocks save after polling reveals a newer plan revision", () => {
  const save = vi.fn();
  const view = render(<TaskStoragePolicySettings detail={detail()} busy={false} onSave={save} />);
  fireEvent.click(screen.getByRole("button", { name: "acquisition.storage.edit" }));
  fireEvent.change(screen.getAllByLabelText("acquisition.storage.downloadTemplate")[0], {
    target: { value: "{Provider}/{OperationId}" },
  });
  view.rerender(
    <TaskStoragePolicySettings
      detail={detail({ task: { id: 2, planRevision: 5 } })}
      busy={false}
      onSave={save}
    />,
  );
  expect(screen.getByRole("button", { name: "acquisition.storage.save" })).toBeDisabled();
  expect(screen.getByText("acquisition.storage.stale")).toBeVisible();
  expect(screen.getAllByDisplayValue("{Provider}/{OperationId}")[0]).toBeVisible();
  expect(save).not.toHaveBeenCalled();
});
it("shows frozen effective overrides without offering edits after execution starts", () => {
  render(
    <TaskStoragePolicySettings
      detail={detail({ canEditStoragePolicy: false })}
      busy={false}
      onSave={vi.fn()}
    />,
  );
  expect(screen.queryByRole("button", { name: "acquisition.storage.edit" })).toBeNull();
  expect(screen.getByText("acquisition.storage.sourceCleanup.quarantine")).toBeVisible();
  expect(screen.getByText("acquisition.storage.frozenHint")).toBeVisible();
});
