import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import ImportLegacyTasksModal from "../components/ImportLegacyTasksModal";
const api = vi.hoisted(() => ({ preview: vi.fn(), import: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({
  default: {
    acquisition: {
      searchAcquisitionCandidates: api.preview,
      importLegacyAcquisitionOverview: api.import,
    },
  },
}));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ children, onOk, footer }: any) => (
    <div>
      {children}
      <button disabled={footer.okProps.isDisabled} onClick={onOk}>
        Confirm import
      </button>
    </div>
  ),
  Button: ({ children, onPress }: any) => <button onClick={onPress}>{children}</button>,
  Spinner: () => <span>loading</span>,
  Checkbox: ({ children, isSelected, onValueChange }: any) => (
    <label>
      <input
        type="checkbox"
        checked={isSelected}
        onChange={(e) => onValueChange(e.target.checked)}
      />
      {children}
    </label>
  ),
  Pagination: () => null,
  toast: { success: vi.fn(), danger: vi.fn() },
}));
beforeEach(() => {
  vi.clearAllMocks();
  api.preview.mockResolvedValue({
    code: 0,
    data: { items: [{ resourceId: 11, resourceName: "Old entry" }], totalCount: 1 },
  });
  api.import.mockResolvedValue({
    code: 0,
    data: { created: 0, matched: 1, taskIds: [42], problems: [] },
  });
});
it("previews only when explicitly opened and imports selected snapshots after confirmation", async () => {
  expect(api.preview).not.toHaveBeenCalled();
  const onImported = vi.fn();
  render(<ImportLegacyTasksModal onImported={onImported} />);
  await screen.findByText("Old entry");
  expect(api.preview).toHaveBeenCalledWith({ page: 1, pageSize: 24, filter: "all" });
  expect(api.import).not.toHaveBeenCalled();
  expect(screen.getByText("Confirm import")).toBeDisabled();
  fireEvent.click(screen.getByRole("checkbox", { name: /Old entry/ }));
  fireEvent.click(screen.getByText("Confirm import"));
  await waitFor(() => expect(api.import).toHaveBeenCalledWith({ resourceIds: [11] }));
  expect(onImported).toHaveBeenCalledTimes(1);
});
