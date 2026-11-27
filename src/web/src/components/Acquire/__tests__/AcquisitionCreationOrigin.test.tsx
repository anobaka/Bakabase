import type { ReactNode } from "react";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter, useLocation } from "react-router-dom";
import { expect, it, vi } from "vitest";
import AcquisitionCreationOrigin from "../AcquisitionCreationOrigin";
import type { PlanTask } from "../PlanTask/models";

const { lookup, createPortal } = vi.hoisted(() => ({
  lookup: vi.fn(),
  createPortal: vi.fn(),
}));
vi.mock("@/sdk/BApi", () => ({ default: { postParser: { searchPostParserTasks: lookup } } }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal }),
}));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ children, title }: { children: ReactNode; title: string }) => <div role="dialog" aria-label={title}>{children}</div>,
  Spinner: () => <span>loading</span>,
  Button: ({ children, onPress }: { children: ReactNode; onPress: () => void }) => (
    <button onClick={onPress}>{children}</button>
  ),
}));
vi.mock("@/components/ExternalLink", () => ({
  default: ({ children, href }: { children: ReactNode; href: string }) => (
    <a href={href}>{children}</a>
  ),
}));
const origin = {
  kind: "postParser",
  module: "postParser",
  entityKind: "task",
  entityId: "411",
  revision: "5",
  title: "Original post",
  url: "https://example.test/post/2665675",
  sourceState: "available",
} as NonNullable<PlanTask["creationOrigins"]>[number];
function Location() {
  const current = useLocation();
  return <output>{current.pathname + current.search}</output>;
}

it("opens parsed content on the acquisition page and confirms before navigating to the exact parsing task", async () => {
  lookup.mockResolvedValue({ code: 0, data: { items: [{ id: 411, text: "Parsed post content" }] } });
  const destroy = vi.fn();
  createPortal.mockReturnValue({ destroy });
  render(
    <MemoryRouter initialEntries={["/acquisitions"]}>
      <AcquisitionCreationOrigin origins={[origin]} compact />
      <Location />
    </MemoryRouter>,
  );
  fireEvent.click(screen.getByRole("button", { name: "acquisition.origin.postTask" }));
  await act(() => vi.dynamicImportSettled());
  const modal = within(await screen.findByRole("dialog", { name: "acquisition.origin.postTask" }));
  expect(await modal.findByText("Parsed post content")).toBeVisible();
  expect(modal.getByRole("link", { name: "acquisition.origin.openPost" })).toHaveAttribute(
    "href",
    origin.url,
  );
  expect(screen.getByText("/acquisitions")).toBeVisible();
  fireEvent.click(modal.getByRole("button", { name: "acquisition.origin.openPostTask" }));
  expect(createPortal).toHaveBeenCalledWith(expect.anything(), expect.objectContaining({
    title: "common.confirm.navigateAway",
    footer: { actions: ["cancel", "ok"] },
  }));
  expect(screen.getByText("/acquisitions")).toBeVisible();
  act(() => createPortal.mock.calls[0][1].onOk());
  expect(destroy).toHaveBeenCalledOnce();
  expect(screen.getByText("/post-parser?taskId=411")).toBeVisible();
});

it("keeps a retained original post URL usable when the source task was deleted", () => {
  render(<AcquisitionCreationOrigin origins={[{ ...origin, sourceState: "deleted" }]} />);
  expect(screen.queryByRole("button")).toBeNull();
  expect(screen.getByText("acquisition.origin.state.deleted")).toBeVisible();
  expect(screen.getByRole("link", { name: "acquisition.origin.openPost" })).toHaveAttribute(
    "href",
    origin.url,
  );
});

it("does not invent a source task link from an unbound snapshot or permit executable original URLs", () => {
  render(
    <AcquisitionCreationOrigin
      origins={[
        { ...origin, entityId: undefined, sourceState: "snapshot", url: "javascript:alert(1)" },
      ]}
    />,
  );
  expect(screen.queryByRole("button")).toBeNull();
  expect(screen.queryByRole("link")).toBeNull();
  expect(screen.getByText("acquisition.origin.state.snapshot")).toBeVisible();
});

it("keeps the list compact while detail shows retained title and revision", () => {
  const view = render(
    <AcquisitionCreationOrigin origins={[{ ...origin, sourceState: "missing" }]} compact />,
  );
  expect(screen.queryByText("Original post")).toBeNull();
  expect(screen.queryByText("acquisition.origin.revision")).toBeNull();
  view.rerender(<AcquisitionCreationOrigin origins={[{ ...origin, sourceState: "missing" }]} />);
  expect(screen.getByText("Original post")).toBeVisible();
  expect(screen.getByText("acquisition.origin.revision")).toBeVisible();
});

it("inspects only the linked post, displays saved extraction passwords and preserves the acquisition route", async () => {
  lookup.mockResolvedValue({ code: 0, data: { items: [{
    id: 411, revision: 6, text: "Text from the saved post", link: origin.url,
    results: { DownloadInfo: { data: { resources: [
      { code: "share-code-only", password: "outer-password", extraction: { steps: [
        { op: "extractArchive", password: "inner-password" },
      ] } },
    ] } } },
  }] } });
  render(<MemoryRouter initialEntries={["/acquisitions"]}>
    <AcquisitionCreationOrigin origins={[{ ...origin, url: undefined }]} compact /><Location />
  </MemoryRouter>);
  fireEvent.click(screen.getByRole("button", { name: "acquisition.origin.postTask" }));
  expect(await screen.findByText("inner-password")).toBeVisible();
  expect(screen.getByText("outer-password")).toBeVisible();
  expect(screen.getByText("Text from the saved post")).toBeVisible();
  expect(screen.getByText("acquisition.origin.postRevisionChanged")).toBeVisible();
  expect(screen.getByRole("link", { name: "acquisition.origin.openPost" })).toHaveAttribute("href", origin.url);
  expect(screen.getByText("/acquisitions")).toBeVisible();
  expect(lookup).toHaveBeenCalledWith({ locateTaskId: 411, pageIndex: 1, pageSize: 1 }, expect.objectContaining({ sensitiveResponse: true }));
});

it("does not display a nearby search result when the source post was removed", async () => {
  lookup.mockResolvedValue({ code: 0, data: { items: [{ id: 412, text: "Other post" }] } });
  render(<MemoryRouter>
    <AcquisitionCreationOrigin origins={[origin]} compact />
  </MemoryRouter>);
  fireEvent.click(screen.getByRole("button", { name: "acquisition.origin.postTask" }));
  expect(await screen.findByRole("alert")).toHaveTextContent("acquisition.origin.postUnavailable");
  expect(screen.queryByText("Other post")).toBeNull();
});
