import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import ManualSourceAction from "../ManualSourceAction";

vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, isDisabled }: any) => <button onClick={onPress} disabled={isDisabled}>{children}</button>,
  Input: ({ label, value, onValueChange, type }: any) => <input aria-label={label} type={type} value={value} onChange={e => onValueChange(e.target.value)} />,
  Modal: ({ title, children, footer }: any) => <section role="dialog" aria-label={title}>{children}{footer}</section>,
}));

const link = "https://download.example.test/official/signed-resource.zip";
const open = (onUse = vi.fn().mockResolvedValue(undefined)) => {
  render(<ManualSourceAction disabled={false} onUse={onUse} />);
  fireEvent.click(screen.getByRole("button", { name: "acquisition.manualSource.title" }));
  return { onUse, url: screen.getByLabelText("acquisition.manualSource.url"),
    save: screen.getByRole("button", { name: "acquisition.sourceImage.use" }) };
};

it("saves an explicit replacement only after confirmation and clears the previous source's code", async () => {
  const { onUse, url, save } = open();
  expect(save).toBeDisabled();
  fireEvent.change(url, { target: { value: link } });
  expect(onUse).not.toHaveBeenCalled();
  fireEvent.click(save);
  await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  expect(onUse).toHaveBeenCalledExactlyOnceWith({ url: link, accessCode: "" });
});

it("rejects unsupported schemes and embedded credentials before submission", () => {
  const { onUse, url, save } = open();
  for (const value of ["file:///folder/file.zip", "https://user:pass@example.test/a.zip", "unparsed"]) {
    fireEvent.change(url, { target: { value } });
    expect(save).toBeDisabled();
  }
  expect(onUse).not.toHaveBeenCalled();
});

it("preserves form values on rejected source verification without starting or replacing a task", async () => {
  const { onUse, url, save } = open(vi.fn().mockRejectedValue(new Error("SOURCE_REPLACEMENT_UNSUPPORTED")));
  fireEvent.change(url, { target: { value: link } });
  fireEvent.change(screen.getByLabelText("acquisition.sourceCorrection.accessCode"), { target: { value: "1234" } });
  fireEvent.click(save);
  expect(await screen.findByRole("alert")).toHaveTextContent("acquisition.manualSource.unsupported");
  expect(onUse).toHaveBeenCalledExactlyOnceWith({ url: link, accessCode: "1234" });
  expect(url).toHaveValue(link);
});

it("does not open a replacement while another mutation is busy", () => {
  render(<ManualSourceAction disabled onUse={vi.fn()} />);
  expect(screen.getByRole("button", { name: "acquisition.manualSource.title" })).toBeDisabled();
});
