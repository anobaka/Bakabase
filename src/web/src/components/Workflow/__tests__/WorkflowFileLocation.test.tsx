import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import WorkflowFileLocation from "../WorkflowFileLocation";

const mocks = vi.hoisted(() => ({ copy: vi.fn(), browser: vi.fn() }));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/core/clipboard", () => ({ copyTextToClipboard: mocks.copy }));
vi.mock("@/components/FilePathValue", () => ({ default: ({ path, pathActions }: any) => <span>{path}{pathActions}</span> }));
vi.mock("../WorkflowArtifactBrowserModal", () => ({
  default: (props: any) => {
    mocks.browser(props);
    return (
      <div role="dialog">
        Browsing {props.location.path}
        <button onClick={props.onClose}>Close browser</button>
      </div>
    );
  },
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, "aria-label": label }: any) => (
    <button aria-label={label} onClick={onPress}>
      {children}
    </button>
  ),
  Spinner: () => <span>Loading</span>,
  Tooltip: ({ children }: any) => children,
}));
beforeEach(() => {
  vi.clearAllMocks();
  mocks.copy.mockResolvedValue(undefined);
});

it("opens the available directory in a separate large browser and closes without changing the task", async () => {
  render(<WorkflowFileLocation path="/nas/delivered/task [A1]" available />);
  expect(mocks.browser).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "workflow.location.browseFolder" }));
  expect(await screen.findByText("Browsing /nas/delivered/task [A1]")).toBeVisible();
  expect(mocks.browser.mock.lastCall?.[0]).toMatchObject({
    location: { path: "/nas/delivered/task [A1]" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Close browser" }));
  expect(screen.queryByText(/^Browsing /)).toBeNull();
});

it("keeps planned, historical and unavailable directories copyable without an open action", async () => {
  render(<WorkflowFileLocation path="/not-yet-created" available={false} />);
  expect(screen.queryByRole("button", { name: "workflow.location.browseFolder" })).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "workflow.location.copyPath" }));
  await waitFor(() => expect(mocks.copy).toHaveBeenCalledWith("/not-yet-created"));
  expect(mocks.browser).not.toHaveBeenCalled();
});

it("forwards current phase identity to the same large browser", async () => {
  render(<WorkflowFileLocation path="/work/current" available taskDirectory={{ taskId: 10, runId: 20 }} />);
  fireEvent.click(screen.getByRole("button", { name: "workflow.location.browseFolder" }));
  await screen.findByText("Browsing /work/current");
  expect(mocks.browser.mock.lastCall?.[0]).toMatchObject({ taskDirectory: { taskId: 10, runId: 20 } });
});
