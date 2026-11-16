import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import i18n from "@/i18n";
import cn from "@/locales/cn/pages/acquisitionDirectories.json";
import en from "@/locales/en/pages/acquisitionDirectories.json";
import TaskDirectoriesModal from "../TaskDirectoriesModal";

// Load the application's resource registration rather than the default key-only mock.
vi.mock("react-i18next", async importOriginal => await importOriginal());
const api = vi.hoisted(() => ({ get: vi.fn(), apply: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({ default: { acquisition: {
  getAcquisitionStageDirectories: api.get, applyAcquisitionStageDirectoryChange: api.apply,
} } }));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ children, title, footer }: any) => <div role="dialog" aria-label={title}>{children}{footer}</div>,
  Button: ({ children, onPress, isDisabled }: any) => <button disabled={isDisabled} onClick={onPress}>{children}</button>,
  Checkbox: ({ children, isSelected, onValueChange }: any) => <label><input type="checkbox" checked={isSelected}
    onChange={event => onValueChange(event.target.checked)} />{children}</label>,
  Spinner: () => <span role="status">loading</span>,
}));
vi.mock("@/components/FileSystemSelector/components/Button", () => ({ default: (props: any) =>
  <button aria-label={props["aria-label"]} onClick={() => props.fileSystemSelectorProps.onSelected({ path: "/new-processing" })}>
    {props.fileSystemSelectorProps.defaultSelectedPath}
  </button>,
}));
afterEach(cleanup);

it.each(["zh-CN", "cn", "en-US", "en"])("registers every task-directory key in the actual %s application resources", language => {
  const dictionary = language === "zh-CN" || language === "cn" ? cn : en;
  expect(Object.keys(cn).sort()).toEqual(Object.keys(en).sort());
  for (const [key, value] of Object.entries(dictionary)) {
    expect(i18n.getResource(language, "translation", key), key).toBe(value);
    expect(i18n.t(key, { lng: language, stage: "Processing" }), key).not.toBe(key);
  }
});

it.each(["zh-CN", "en-US"])("renders directory choices and server errors using the production %s translations", async language => {
  await i18n.changeLanguage(language);
  const dictionary = language === "zh-CN" ? cn : en;
  api.get.mockResolvedValue({ code: 0, data: { taskId: 1, expectedRevision: 1, canChange: true, blockers: [],
    parts: [{ partId: "main", downloadDirectory: "/download", processingDirectory: "/processing", deliveryDirectory: "/delivery" }],
  } });
  api.apply.mockResolvedValue({ code: 1, message: "STAGE_DIRECTORY_TARGET_CONFLICT" });
  render(<TaskDirectoriesModal taskId={1} onChanged={vi.fn()} onClose={vi.fn()} />);
  expect(screen.getByRole("dialog", { name: dictionary["acquisition.directories.title"] })).toBeVisible();
  await screen.findByText("/processing");
  expect(screen.getAllByLabelText(dictionary["acquisition.directories.moveFiles"])).toHaveLength(3);
  const processing = i18n.t("acquisition.timeline.stage.processing");
  fireEvent.click(screen.getByRole("button", { name: i18n.t("acquisition.directories.select", { stage: processing }) }));
  fireEvent.click(screen.getByRole("button", { name: i18n.t("common.action.apply") }));
  expect(await screen.findByRole("alert")).toHaveTextContent(dictionary["acquisition.directories.error.STAGE_DIRECTORY_TARGET_CONFLICT"]);
  expect(screen.getByRole("dialog").textContent).not.toContain("acquisition.directories.");
});
