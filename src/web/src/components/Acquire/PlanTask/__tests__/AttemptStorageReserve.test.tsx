import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import useAttemptStorageReserve, { type AttemptRunOptions } from "../useAttemptStorageReserve";

const getOptions = vi.hoisted(() => vi.fn());
vi.mock("@/sdk/BApi", () => ({default:{acquisition:{getAcquisitionOptions:getOptions}}}));
vi.mock("@/components/bakaui", () => ({
  Button: ({children,onPress,isDisabled}:any) => <button disabled={isDisabled} onClick={onPress}>{children}</button>,
  Spinner: ({"aria-label":label}:any) => <span role="status" aria-label={label} />,
  Tooltip: ({children}:any) => <>{children}</>,
  Checkbox: ({children,isSelected,isDisabled,onValueChange}:any) => <label>
    <input type="checkbox" checked={isSelected} disabled={isDisabled}
      onChange={event => onValueChange(event.target.checked)} />{children}
  </label>,
}));

function Harness({submit}:{submit:(options:AttemptRunOptions)=>void}) {
  const reserve = useAttemptStorageReserve(false);
  return <>{reserve.control}<button disabled={!reserve.ready}
    onClick={() => submit(reserve.options!)}>start</button></>;
}
beforeEach(() => getOptions.mockReset());

it("waits for the real options read and preserves an unchecked global emergency default", async () => {
  let resolve!: (value: unknown) => void;
  getOptions.mockReturnValue(new Promise(r => {resolve=r;}));
  const submit = vi.fn();
  render(<Harness submit={submit} />);
  expect(screen.getByRole("button",{name:"start"})).toBeDisabled();
  await act(async () => resolve({code:0,data:{minimumFreeSpaceBytes:50*1024**3,useEmergencySpaceReserve:true}}));
  const checkbox = screen.getByRole("checkbox",{name:"acquisition.reserve.operation.label"});
  expect(checkbox).toBeChecked();
  fireEvent.click(checkbox);
  fireEvent.click(screen.getByRole("button",{name:"start"}));
  expect(submit).toHaveBeenCalledExactlyOnceWith({useEmergencySpaceReserve:false});
  expect(getOptions).toHaveBeenCalledOnce();
});

it("blocks failed or incomplete defaults and retries without assuming the global setting", async () => {
  getOptions.mockResolvedValueOnce({code:0,data:{minimumFreeSpaceBytes:50*1024**3}})
    .mockResolvedValueOnce({code:0,data:{minimumFreeSpaceBytes:50*1024**3,useEmergencySpaceReserve:false}});
  const submit = vi.fn();
  render(<Harness submit={submit} />);
  fireEvent.click(await screen.findByRole("button",{name:"acquisition.reserve.reload"}));
  await waitFor(() => expect(screen.getByRole("button",{name:"start"})).toBeEnabled());
  expect(screen.getByRole("checkbox",{name:"acquisition.reserve.operation.label"})).not.toBeChecked();
  fireEvent.click(screen.getByRole("button",{name:"start"}));
  expect(submit).toHaveBeenCalledExactlyOnceWith({useEmergencySpaceReserve:false});
  expect(getOptions).toHaveBeenCalledTimes(2);
});
