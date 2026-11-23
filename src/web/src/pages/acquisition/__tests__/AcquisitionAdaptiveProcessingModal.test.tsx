import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import AcquisitionAdaptiveProcessingModal from "../components/AcquisitionAdaptiveProcessingModal";
import cn from "@/locales/cn/pages/acquisition.json";

const api = vi.hoisted(() => ({ preview: vi.fn(), apply: vi.fn() }));
vi.mock("../components/acquisitionAdaptiveProcessingApi", () => ({
  acquisitionAdaptiveProcessingApi: api,
}));
vi.mock("../components/AcquisitionContinuationMethodTabs", () => ({ default: () => null }));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, values?: Record<string, unknown>) =>
      String((cn as Record<string, string>)[key] ?? values?.defaultValue ?? key).replace(
        /{{(\w+)}}/g,
        (_, name) => String(values?.[name] ?? name),
      ),
  }),
}));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ children, footer }: any) => (
    <div role="dialog">
      {children}
      <footer>{footer}</footer>
    </div>
  ),
  Button: ({ children, onPress, isDisabled, isLoading }: any) => (
    <button disabled={isDisabled || isLoading} onClick={onPress}>
      {children}
    </button>
  ),
  Checkbox: ({ children, isSelected, onValueChange, isDisabled }: any) => (
    <label>
      <input
        type="checkbox"
        checked={isSelected}
        disabled={isDisabled}
        onChange={(event) => onValueChange(event.target.checked)}
      />
      {children}
    </label>
  ),
  Chip: ({ children }: any) => <span>{children}</span>,
  Tooltip: ({ children }: any) => children,
  Spinner: () => <span>Loading</span>,
  Select: ({ selectedKeys, dataSource, onSelectionChange, "aria-label": label }: any) => (
    <select
      aria-label={label}
      value={[...selectedKeys][0]}
      onChange={(event) => onSelectionChange(new Set([event.target.value]))}
    >
      {dataSource.map((item: any) => (
        <option key={item.value} value={item.value}>
          {item.label}
        </option>
      ))}
    </select>
  ),
}));

it("uses server current-file eligibility for batch AI, keeps the bounded preview token and localizes a changed-directory refusal", async () => {
  api.preview.mockResolvedValue({
    token: "fixed-current-directory-preview",
    rows: [
      { taskId: 865, canMigrate: true, reasonCode: "ADAPTIVE_MIGRATION_PREPARED_LOCAL_READY" },
      { taskId: 832, canMigrate: false, reasonCode: "ADAPTIVE_MIGRATION_RUN_UNSUPPORTED" },
    ],
  });
  api.apply.mockResolvedValue({
    migratedTaskIds: [],
    skipped: [
      {
        taskId: 865,
        canMigrate: false,
        reasonCode: "ADAPTIVE_MIGRATION_FAILED",
        reason: "PLAN_CURRENT_DIRECTORY_CHANGED",
      },
    ],
  });
  render(<AcquisitionAdaptiveProcessingModal taskIds={[865, 832]} />);
  const ready = (await screen.findByText("#865")).closest("div")!;
  const blocked = screen.getByText("#832").closest("div")!;
  expect(within(ready).getByRole("checkbox")).toBeChecked();
  expect(
    within(ready).getByText(
      cn["acquisition.adaptive.reason.ADAPTIVE_MIGRATION_PREPARED_LOCAL_READY"],
    ),
  ).toBeVisible();
  expect(within(blocked).getByRole("checkbox")).toBeDisabled();
  expect(screen.getByText(cn["acquisition.adaptive.hint"])).toHaveTextContent("读取任务当前目录");
  expect(screen.getByText(cn["acquisition.adaptive.hint"])).toHaveTextContent("达到分析次数上限");
  expect(api.preview).toHaveBeenCalledWith([865, 832], 3, expect.any(AbortSignal));
  const footer = within(screen.getByRole("dialog").querySelector("footer")!);
  fireEvent.click(footer.getAllByRole("button")[1]);
  await waitFor(() =>
    expect(api.apply).toHaveBeenCalledExactlyOnceWith("fixed-current-directory-preview", [865]),
  );
  expect(await screen.findByText(/本次输入目录发生变化/)).toBeVisible();
});

it("explains that adaptive preview is a read-only batch diagnosis while loading", async () => {
  let resolvePreview!: (value: any) => void;
  api.preview.mockReturnValue(
    new Promise((resolve) => {
      resolvePreview = resolve;
    }),
  );
  render(<AcquisitionAdaptiveProcessingModal taskIds={[865, 832]} />);
  expect(
    screen.getByText(cn["acquisition.adaptive.previewing"].replace("{{count}}", "2")),
  ).toBeVisible();
  resolvePreview({ token: "preview", rows: [] });
  await waitFor(() =>
    expect(
      screen.queryByText(cn["acquisition.adaptive.previewing"].replace("{{count}}", "2")),
    ).not.toBeInTheDocument(),
  );
});
