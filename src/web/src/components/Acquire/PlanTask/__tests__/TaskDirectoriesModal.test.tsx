import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import TaskDirectoriesModal from "../TaskDirectoriesModal";

const mocks = vi.hoisted(() => ({get:vi.fn(),apply:vi.fn(),path:"/new/processing"}));
vi.mock("react-i18next",()=>({useTranslation:()=>({t:(key:string,args?:any)=>args?.stage ? `${key}:${args.stage}` : key})}));
vi.mock("@/sdk/BApi",()=>({default:{acquisition:{getAcquisitionStageDirectories:mocks.get,applyAcquisitionStageDirectoryChange:mocks.apply}}}));
vi.mock("@/components/bakaui",()=>({
  Modal:({children,footer,title}:any)=><div role="dialog" aria-label={title}>{children}{footer}</div>,
  Button:({children,onPress,isDisabled,isLoading}:any)=><button disabled={isDisabled||isLoading} onClick={onPress}>{children}</button>,
  Checkbox:({children,isSelected,onValueChange,isDisabled}:any)=><label><input type="checkbox" checked={isSelected} disabled={isDisabled} onChange={e=>onValueChange(e.target.checked)}/>{children}</label>,
  Spinner:()=> <span role="status">loading</span>,
}));
vi.mock("@/components/FileSystemSelector/components/Button",()=>({default:({fileSystemSelectorProps:p,isDisabled,...props}:any)=><button disabled={isDisabled} aria-label={props["aria-label"]} onClick={()=>p.onSelected({path:mocks.path})}>{p.defaultSelectedPath||"empty"}</button>}));
const snapshot={taskId:1,expectedRevision:7,canChange:true,blockers:[],parts:[{partId:"p",title:"Files",downloadDirectory:"/download",processingDirectory:"/process",deliveryDirectory:"/delivery"}]};
afterEach(cleanup);
beforeEach(()=>{vi.clearAllMocks();mocks.path="/new/processing";mocks.get.mockResolvedValue({code:0,data:snapshot});mocks.apply.mockResolvedValue({code:0,data:{taskId:1}});});

it("uses the existing folder selector and defaults each changed stage to moving files without triggering execution",async()=>{
  const changed=vi.fn(),close=vi.fn();render(<TaskDirectoriesModal taskId={1} onChanged={changed} onClose={close}/>);
  await screen.findByText("/process");
  expect(screen.getAllByRole("checkbox").every(item=>(item as HTMLInputElement).checked)).toBe(true);
  expect(screen.getByRole("button",{name:"common.action.apply"})).toBeDisabled();
  fireEvent.click(screen.getByRole("button",{name:"acquisition.directories.select:acquisition.timeline.stage.processing"}));
  fireEvent.click(screen.getByRole("button",{name:"common.action.apply"}));
  await waitFor(()=>expect(mocks.apply).toHaveBeenCalledWith(1,{expectedRevision:7,parts:[{partId:"p",download:null,processing:{directory:"/new/processing",moveFiles:true},delivery:null}]},expect.objectContaining({sensitiveResponse:true})));
  expect(changed).toHaveBeenCalledOnce();expect(close).toHaveBeenCalledOnce();
});

it("lets the user keep files in place while adopting an existing directory",async()=>{
  render(<TaskDirectoriesModal taskId={1} onChanged={vi.fn()} onClose={vi.fn()}/>);await screen.findByText("/process");
  fireEvent.click(screen.getByRole("button",{name:"acquisition.directories.select:acquisition.timeline.stage.processing"}));
  fireEvent.click(screen.getAllByRole("checkbox")[1]);
  fireEvent.click(screen.getByRole("button",{name:"common.action.apply"}));
  await waitFor(()=>expect(mocks.apply).toHaveBeenCalledOnce());
  expect(mocks.apply.mock.calls[0][1].parts[0].processing.moveFiles).toBe(false);
});

it("allows explicitly choosing the same legacy path so its binding can be confirmed", async()=>{
  mocks.path="/process";
  render(<TaskDirectoriesModal taskId={1} onChanged={vi.fn()} onClose={vi.fn()}/>);await screen.findByText("/process");
  expect(screen.getByRole("button",{name:"common.action.apply"})).toBeDisabled();
  fireEvent.click(screen.getByRole("button",{name:"acquisition.directories.select:acquisition.timeline.stage.processing"}));
  fireEvent.click(screen.getByRole("button",{name:"common.action.apply"}));
  await waitFor(()=>expect(mocks.apply).toHaveBeenCalledOnce());
  expect(mocks.apply.mock.calls[0][1].parts[0].processing).toEqual({directory:"/process",moveFiles:true});
});

it("resumes only the persisted intent after an interrupted move",async()=>{
  const pending={expectedRevision:7,parts:[{partId:"p",processing:{directory:"/unfinished",moveFiles:true}}]};
  mocks.get.mockResolvedValue({code:0,data:{...snapshot,canChange:false,pendingChange:pending}});
  render(<TaskDirectoriesModal taskId={1} onChanged={vi.fn()} onClose={vi.fn()}/>);await screen.findByText("/unfinished");
  expect(screen.getAllByRole("checkbox").every(item=>(item as HTMLInputElement).disabled)).toBe(true);
  fireEvent.click(screen.getByRole("button",{name:"acquisition.directories.resume"}));
  await waitFor(()=>expect(mocks.apply).toHaveBeenCalledWith(1,pending,expect.anything()));
});
