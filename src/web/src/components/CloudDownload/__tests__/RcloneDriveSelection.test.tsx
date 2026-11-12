import { Children, useState } from "react";
import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import RcloneDriveSelection from "../RcloneDriveSelection";

const api = vi.hoisted(() => ({
  getRcloneAuthorizationDrives: vi.fn(),
  previewRcloneAuthorizationDrive: vi.fn(),
}));
vi.mock("@/sdk/BApi", () => ({ default: { rclone: api } }));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, isDisabled }: any) => (
    <button disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Chip: ({ children }: any) => <span>{children}</span>,
  Spinner: () => <span>Loading</span>,
  Select: ({ label, selectedKeys, dataSource, isDisabled, onSelectionChange }: any) => (
    <select
      aria-label={label}
      value={selectedKeys[0] ?? ""}
      disabled={isDisabled}
      onChange={(event) => onSelectionChange(new Set([event.target.value]))}
    >
      <option value="" />
      {dataSource.map((item: any) => (
        <option key={item.value} value={item.value}>
          {item.label}
        </option>
      ))}
    </select>
  ),
  Accordion: ({ children, selectedKeys, onSelectionChange }: any) => (
    <div>
      {Children.map(children, (child: any) => (
        <div>
          <button
            onClick={() =>
              onSelectionChange(new Set(selectedKeys.includes(child.key) ? [] : [child.key]))
            }
          >
            {child.props.title}
          </button>
          {selectedKeys.includes(child.key) && child.props.children}
        </div>
      ))}
    </div>
  ),
  AccordionItem: ({ children }: any) => <div>{children}</div>,
}));
const question = {
  id: "question-a",
  name: "config_driveid",
  help: "",
  type: "string",
  required: true,
  isSecret: false,
  exclusive: false,
  defaultValue: "drive-a",
  examples: [
    { value: "drive-a", help: "My OneDrive (personal)" },
    { value: "drive-b", help: "Documents (documentLibrary)" },
    { value: "drive-c", help: "System library (documentLibrary)" },
  ],
};
const candidates = [
  { driveId: "drive-a", name: "My OneDrive", isPrimary: false, accessStatus: "unchecked" },
  { driveId: "drive-b", name: "Documents", isPrimary: false, accessStatus: "unchecked" },
  { driveId: "drive-c", name: "System library", isPrimary: false, accessStatus: "unchecked" },
];
const deferred = () => {
  let resolve!: (value: unknown) => void;
  const promise = new Promise((done) => {
    resolve = done;
  });
  return { promise, resolve };
};
function Fixture({ questionId = question.id }: { questionId?: string }) {
  const [answer, setAnswer] = useState("");
  return (
    <>
      <RcloneDriveSelection
        key={questionId}
        sessionId="session"
        question={{ ...question, id: questionId }}
        answer={answer}
        onAnswer={setAnswer}
        isDisabled={false}
        problemLabel={(message) => message}
      />
      <output aria-label="answer">{answer}</output>
    </>
  );
}
const openOther = () => {
  fireEvent.click(screen.getByText("rclone.authorization.otherDrives"));
  return screen.getByRole("combobox", { name: "rclone.authorization.question.config_driveid" });
};
beforeEach(() => {
  api.getRcloneAuthorizationDrives.mockReset().mockImplementation(async (_id, query) => ({
    code: 0,
    data: { questionId: query.questionId, candidates },
  }));
  api.previewRcloneAuthorizationDrive.mockReset();
});

it("only recommends an accessible server-verified primary, keeping every other distinct candidate under Advanced", async () => {
  api.getRcloneAuthorizationDrives.mockResolvedValue({
    code: 0,
    data: {
      questionId: question.id,
      candidates: candidates.map((candidate) =>
        candidate.driveId === "drive-b"
          ? { ...candidate, isPrimary: true, accessStatus: "accessible" }
          : candidate,
      ),
    },
  });
  render(<Fixture />);
  const primary = await screen.findByRole("combobox", { name: "rclone.authorization.myOneDrive" });
  await waitFor(() => expect(primary).toHaveValue("drive-b"));
  expect(screen.getByLabelText("answer")).toHaveTextContent("drive-b");
  expect(openOther().querySelectorAll("option")).toHaveLength(3);
  expect(screen.getByRole("option", { name: /System library/ })).toHaveValue("drive-c");
  expect(api.previewRcloneAuthorizationDrive).not.toHaveBeenCalled();
});

it("never infers a primary from a familiar name or unchecked primary metadata", async () => {
  api.getRcloneAuthorizationDrives.mockResolvedValue({
    code: 0,
    data: {
      questionId: question.id,
      candidates: candidates.map((candidate) => ({ ...candidate, isPrimary: true })),
    },
  });
  render(<Fixture />);
  await screen.findByText("rclone.authorization.noVerifiedPrimary");
  expect(screen.queryByRole("combobox", { name: "rclone.authorization.myOneDrive" })).toBeNull();
  expect(openOther()).toHaveValue("");
  expect(screen.getByLabelText("answer")).toBeEmptyDOMElement();
});

it("does not overwrite a user selection when primary verification completes late", async () => {
  const pending = deferred();
  api.getRcloneAuthorizationDrives.mockReturnValueOnce(pending.promise);
  render(<Fixture />);
  fireEvent.change(openOther(), { target: { value: "drive-c" } });
  await act(async () =>
    pending.resolve({
      code: 0,
      data: {
        questionId: question.id,
        candidates: candidates.map((candidate) =>
          candidate.driveId === "drive-b"
            ? { ...candidate, isPrimary: true, accessStatus: "accessible" }
            : candidate,
        ),
      },
    }),
  );
  expect(screen.getByLabelText("answer")).toHaveTextContent("drive-c");
  expect(screen.getByRole("combobox", { name: "rclone.authorization.myOneDrive" })).toHaveValue("");
});

it("ignores a previous drive's late preview while retaining the new drive's successful preview", async () => {
  const pending = deferred();
  api.previewRcloneAuthorizationDrive.mockReturnValueOnce(pending.promise).mockResolvedValueOnce({
    code: 0,
    data: {
      questionId: question.id,
      driveId: "drive-b",
      items: [{ name: "New root folder", isDirectory: true }],
      hasMore: true,
    },
  });
  render(<Fixture />);
  await screen.findByText("rclone.authorization.noVerifiedPrimary");
  const select = openOther();
  fireEvent.change(select, { target: { value: "drive-a" } });
  fireEvent.click(screen.getByText("rclone.authorization.previewRoot"));
  fireEvent.change(select, { target: { value: "drive-b" } });
  const button = screen.getByText("rclone.authorization.previewRoot");
  expect(button).toBeEnabled();
  fireEvent.click(button);
  expect(await screen.findByText("New root folder")).toBeVisible();
  expect(screen.getByText("rclone.authorization.previewHasMore")).toBeVisible();
  await act(async () =>
    pending.resolve({
      code: 0,
      data: {
        questionId: question.id,
        driveId: "drive-a",
        items: [{ name: "Old root secret", isDirectory: false }],
        hasMore: false,
      },
    }),
  );
  expect(screen.queryByText("Old root secret")).toBeNull();
  expect(screen.getByText("New root folder")).toBeVisible();
  expect(api.previewRcloneAuthorizationDrive).toHaveBeenLastCalledWith(
    "session",
    { questionId: question.id, driveId: "drive-b" },
    { showErrorToast: false, sensitiveResponse: true },
  );
});

it("rejects metadata and previews from a previous question without leaving the new question busy", async () => {
  const oldChoices = deferred();
  api.getRcloneAuthorizationDrives.mockReturnValueOnce(oldChoices.promise);
  const view = render(<Fixture />);
  view.rerender(<Fixture questionId="question-b" />);
  await screen.findByText("rclone.authorization.noVerifiedPrimary");
  await act(async () =>
    oldChoices.resolve({
      code: 0,
      data: {
        questionId: question.id,
        candidates: [{ ...candidates[0], isPrimary: true, accessStatus: "accessible" }],
      },
    }),
  );
  expect(screen.queryByRole("combobox", { name: "rclone.authorization.myOneDrive" })).toBeNull();
  const oldPreview = deferred();
  api.previewRcloneAuthorizationDrive.mockReturnValueOnce(oldPreview.promise);
  fireEvent.change(openOther(), { target: { value: "drive-a" } });
  fireEvent.click(screen.getByText("rclone.authorization.previewRoot"));
  view.rerender(<Fixture questionId="question-c" />);
  await screen.findByText("rclone.authorization.noVerifiedPrimary");
  await act(async () => oldPreview.resolve({ code: 500, message: "OLD_QUESTION_FAILURE" }));
  expect(screen.queryByText("OLD_QUESTION_FAILURE")).toBeNull();
  expect(screen.getByText("rclone.authorization.previewRoot")).toBeEnabled();
});

it("fetches metadata once per question, retries explicitly, and isolates a failed preview to that candidate", async () => {
  api.getRcloneAuthorizationDrives.mockResolvedValueOnce({
    code: 500,
    message: "RCLONE_DRIVE_METADATA_UNAVAILABLE",
  });
  const view = render(<Fixture />);
  await screen.findByText("RCLONE_DRIVE_METADATA_UNAVAILABLE");
  view.rerender(<Fixture />);
  expect(api.getRcloneAuthorizationDrives).toHaveBeenCalledTimes(1);
  fireEvent.click(screen.getByText("common.action.retry"));
  await waitFor(() => expect(api.getRcloneAuthorizationDrives).toHaveBeenCalledTimes(2));
  api.previewRcloneAuthorizationDrive.mockResolvedValueOnce({
    code: 500,
    message: "Candidate currently inaccessible",
  });
  fireEvent.change(openOther(), { target: { value: "drive-a" } });
  fireEvent.click(screen.getByText("rclone.authorization.previewRoot"));
  expect(await screen.findByText("Candidate currently inaccessible")).toBeVisible();
  fireEvent.change(
    screen.getByRole("combobox", { name: "rclone.authorization.question.config_driveid" }),
    {
      target: { value: "drive-b" },
    },
  );
  expect(screen.queryByText("Candidate currently inaccessible")).toBeNull();
  expect(screen.getByText("rclone.authorization.driveAccess.unchecked")).toBeVisible();
  expect(screen.getByText("rclone.authorization.previewRoot")).toBeEnabled();
});

it("uses a matching successful preview as access evidence even when metadata failed", async () => {
  api.getRcloneAuthorizationDrives.mockResolvedValueOnce({
    code: 500,
    message: "RCLONE_DRIVE_METADATA_UNAVAILABLE",
  });
  api.previewRcloneAuthorizationDrive.mockResolvedValueOnce({
    code: 0,
    data: { questionId: question.id, driveId: "drive-a", items: [], hasMore: false },
  });
  render(<Fixture />);
  await screen.findByText("RCLONE_DRIVE_METADATA_UNAVAILABLE");
  fireEvent.change(openOther(), { target: { value: "drive-a" } });
  fireEvent.click(screen.getByText("rclone.authorization.previewRoot"));
  expect(await screen.findByText("rclone.authorization.emptyRoot")).toBeVisible();
  expect(screen.getByText("rclone.authorization.driveAccess.accessible")).toBeVisible();
  expect(screen.queryByRole("combobox", { name: "rclone.authorization.myOneDrive" })).toBeNull();
});
