import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import PostImageSourceAction from "../PostImageSourceAction";
import { postImageSourceApi } from "../postImageSourceApi";
vi.mock("../postImageSourceApi", () => ({
  postImageSourceApi: { list: vi.fn(), decode: vi.fn() },
}));
vi.mock("@/components/ExternalLink", () => ({
  default: ({ href, children }: any) => <a href={href}>{children}</a>,
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, isDisabled, ...props }: any) => (
    <button onClick={onPress} disabled={isDisabled} aria-pressed={props["aria-pressed"]}>
      {children}
    </button>
  ),
  Input: ({ label, value, onValueChange }: any) => (
    <input aria-label={label} value={value} onChange={(e) => onValueChange(e.target.value)} />
  ),
  Modal: ({ children, title, footer }: any) => (
    <div role="dialog" aria-label={title}>
      {children}
      {footer}
    </div>
  ),
  Spinner: () => <span>…</span>,
  Select: ({ label, selectedKeys, dataSource, onSelectionChange }: any) => (
    <select
      aria-label={label}
      value={(Array.from(selectedKeys)[0] as string) ?? ""}
      onChange={(e) => onSelectionChange(new Set([e.target.value]))}
    >
      <option value="" />
      {dataSource.map((d: any) => (
        <option value={d.value} key={d.value}>
          {d.label}
        </option>
      ))}
    </select>
  ),
}));
const origin = {
  kind: "postParser",
  entityKind: "task",
  entityId: "58",
  sourceState: "available",
} as const;
const image = "https://example.test/one.png";
const url = "https://example.test/download.zip";
const renderAction = (onUse: any = vi.fn()) => {
  const result = render(
    <PostImageSourceAction
      origins={[origin]}
      imageUrl={image}
      initialAccessCode="post-code"
      onUse={onUse}
    />,
  );
  fireEvent.click(screen.getByRole("button", { name: "acquisition.sourceImage.title" }));
  return result;
};
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(postImageSourceApi.list).mockResolvedValue({
    revision: 3,
    images: [
      { id: "one", url: image, floor: 0 },
      { id: "two", url: "https://example.test/two.png", floor: 1 },
    ],
  });
  vi.mocked(postImageSourceApi.decode).mockResolvedValue({
    status: "decoded",
    sources: [{ url, methodId: "http.native" }],
  });
});
it("loads only image metadata, decodes the selected image on request, and saves only on explicit use", async () => {
  const use = vi.fn();
  renderAction(use);
  const decode = await screen.findByRole("button", { name: "acquisition.sourceImage.decode" });
  expect(postImageSourceApi.decode).not.toHaveBeenCalled();
  fireEvent.click(decode);
  await screen.findByText(new RegExp("download.zip"));
  expect(postImageSourceApi.decode).toHaveBeenCalledWith(
    58,
    3,
    "one",
    expect.any(AbortSignal),
    undefined,
  );
  expect(use).not.toHaveBeenCalled();
  fireEvent.change(screen.getByLabelText("acquisition.draft.accessCode"), {
    target: { value: "corrected" },
  });
  fireEvent.click(screen.getByRole("button", { name: "acquisition.sourceImage.use" }));
  expect(use).toHaveBeenCalledWith({ url, methodId: "http.native", accessCode: "corrected" });
});
it("discards stale decode results when switching images, and shows the local-file fallback", async () => {
  let done: ((r: any) => void) | undefined;
  vi.mocked(postImageSourceApi.decode).mockReturnValueOnce(
    new Promise((resolve) => {
      done = resolve;
    }),
  );
  const { container } = renderAction();
  fireEvent.click(await screen.findByRole("button", { name: "acquisition.sourceImage.decode" }));
  fireEvent.change(screen.getByLabelText("acquisition.sourceImage.image"), {
    target: { value: "two" },
  });
  expect(vi.mocked(postImageSourceApi.decode).mock.calls[0][3].aborted).toBe(true);
  done?.({ status: "decoded", sources: [{ url, methodId: "http.native" }] });
  const file = new File(["png"], "QR.png", { type: "image/png" });
  fireEvent.change(container.querySelector("input[type=file]")!, { target: { files: [file] } });
  await waitFor(() =>
    expect(postImageSourceApi.decode).toHaveBeenCalledWith(
      58,
      3,
      "two",
      expect.any(AbortSignal),
      "cG5n",
    ),
  );
});
it("shows an unsupported or empty QR without offering to save it", async () => {
  vi.mocked(postImageSourceApi.decode).mockResolvedValue({
    status: "unsupportedSource",
    sources: [],
  });
  const use = vi.fn();
  renderAction(use);
  fireEvent.click(await screen.findByRole("button", { name: "acquisition.sourceImage.decode" }));
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "acquisition.sourceImage.status.unsupportedSource",
  );
  expect(screen.getByRole("button", { name: "acquisition.sourceImage.use" })).toBeDisabled();
  expect(use).not.toHaveBeenCalled();
});
