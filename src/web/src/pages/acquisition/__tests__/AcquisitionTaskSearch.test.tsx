import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { useState } from "react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import AcquisitionTaskSearch from "../components/AcquisitionTaskSearch";

const mocks = vi.hoisted(() => ({ get: vi.fn(), open: vi.fn(), search: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({ default: { acquisition: { getAcquisition: mocks.get } } }));
// Exercise the actual HeroUI keyboard/selection behavior, including custom text.
vi.mock("@/components/bakaui", async () => {
  const { Autocomplete, AutocompleteItem } = await import("@heroui/react");
  return { Autocomplete, AutocompleteItem };
});
const candidate = { id: 946, title: "PikPak task", builderId: "post", phase: "attention" };
function Harness() {
  const [value, setValue] = useState("");
  return (
    <AcquisitionTaskSearch
      value={value}
      onValueChange={setValue}
      onSearch={() => mocks.search(value)}
      onOpenTask={mocks.open}
    />
  );
}
function type(value: string) {
  const input = screen.getByRole("combobox");
  act(() => input.focus());
  fireEvent.change(input, { target: { value } });
  return input;
}
beforeEach(() => {
  vi.clearAllMocks();
  mocks.get.mockReset().mockResolvedValue({ code: 0, data: candidate });
  Element.prototype.scrollIntoView = vi.fn();
});
afterEach(cleanup);

it("offers an exact ID candidate without selecting it or replacing a literal hash search", async () => {
  render(<Harness />);
  const input = type("#946");
  await waitFor(() => expect(mocks.get).toHaveBeenCalledTimes(1));
  await screen.findByRole("option", { hidden: true });
  expect(mocks.get).toHaveBeenCalledWith(946, { sensitiveResponse: true });
  expect(input).toHaveValue("#946");
  expect(mocks.open).not.toHaveBeenCalled();
  fireEvent.keyDown(input, { key: "Enter", code: "Enter" });
  expect(mocks.search).toHaveBeenLastCalledWith("#946");
  expect(mocks.open).not.toHaveBeenCalled();
  expect(input).toHaveValue("#946");
  await waitFor(() => expect(input).toHaveAttribute("aria-expanded", "false"));
});

it("opens the explicitly selected task and keeps the search text", async () => {
  render(<Harness />);
  const input = type("#946");
  const option = await screen.findByRole("option", { hidden: true });
  fireEvent.click(option);
  await waitFor(() => expect(mocks.open).toHaveBeenCalledWith(candidate));
  expect(input).toHaveValue("#946");
});

it("allows deliberate keyboard selection with an arrow key and Enter", async () => {
  render(<Harness />);
  const input = type("#946");
  await screen.findByRole("option", { hidden: true });
  fireEvent.keyDown(input, { key: "ArrowDown", code: "ArrowDown" });
  fireEvent.keyDown(input, { key: "Enter", code: "Enter" });
  expect(mocks.open).toHaveBeenCalledWith(candidate);
});

it("keeps arbitrary hash text searchable and rejects stale or out-of-range ID suggestions", async () => {
  let resolve!: (value: unknown) => void;
  mocks.get.mockReturnValueOnce(
    new Promise((done) => {
      resolve = done;
    }),
  );
  render(<Harness />);
  const input = type("#946");
  await waitFor(() => expect(mocks.get).toHaveBeenCalledTimes(1));
  type("Game #946 edition");
  resolve({ code: 0, data: candidate });
  await waitFor(() => expect(input).toHaveValue("Game #946 edition"));
  expect(screen.queryByRole("option")).toBeNull();
  fireEvent.keyDown(input, { key: "Enter", code: "Enter" });
  expect(mocks.search).toHaveBeenLastCalledWith("Game #946 edition");
  type("#2147483648");
  fireEvent.keyDown(input, { key: "Enter", code: "Enter" });
  expect(mocks.search).toHaveBeenLastCalledWith("#2147483648");
  expect(mocks.get).toHaveBeenCalledTimes(1);
});

it("keeps search available when a task lookup fails", async () => {
  mocks.get.mockRejectedValue(new Error("not found"));
  render(<Harness />);
  const input = type("#946");
  await waitFor(() => expect(mocks.get).toHaveBeenCalledTimes(1));
  await waitFor(() => expect(input).toHaveAttribute("aria-expanded", "false"));
  type(" #946");
  expect(input).toHaveAttribute("aria-expanded", "false");
  fireEvent.keyDown(input, { key: "Enter", code: "Enter" });
  expect(mocks.search).toHaveBeenLastCalledWith(" #946");
  expect(mocks.open).not.toHaveBeenCalled();
});

it("keeps the page accessible while submitting a keyword without any task suggestions", async () => {
  render(<><Harness /><button>Selected source</button></>);
  const input = type("beta");
  fireEvent.keyDown(input, { key: "Enter", code: "Enter" });
  expect(mocks.search).toHaveBeenCalledWith("beta");
  expect(input).toHaveFocus();
  expect(input).toHaveAttribute("aria-expanded", "false");
  expect(screen.getByRole("button", { name: "Selected source" })).toBeVisible();
  expect(screen.queryByRole("listbox", { hidden: true })).toBeNull();
});
