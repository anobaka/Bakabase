import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import AssessmentInputOutput from "../AssessmentInputOutput";

const { request } = vi.hoisted(() => ({ request: vi.fn() }));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/sdk/BApi", () => ({ default: { acquisition: { getAcquisitionAssessmentIo: request } } }));
vi.mock("@/components/bakaui", () => ({
  Button: ({children, onPress}: any) => <button onClick={onPress}>{children}</button>,
  Spinner: (props: any) => <div role="status" aria-label={props["aria-label"]} />,
}));
afterEach(cleanup);
beforeEach(() => request.mockReset());
const attempt = { id: "8/1", runId: 8, stage: "processing", attempt: 1, nodes: ["fs.assessDelivery"] } as any;

it("shows actual rejected model values and field diagnostics without enabling global audit", async () => {
  const output = '{"operations":[{"type":"move","inputId":999,"destination":"audio"}]}';
  request.mockResolvedValue({code: 0, data: { calls: [{iteration: 1, inputKind: "recordedFiles",
    inputPreview: '{"prefix":"author/package","folders":[]}', inputTruncated: false,
    outputKind: "modelOutput", outputPreview: output, outputTruncated: false,
    validationError: "operations[0].inputId: expected one file ID between 1 and 27"}]}});
  render(<AssessmentInputOutput taskId={7} attempt={attempt} />);
  expect(await screen.findByText(output)).toBeVisible();
  expect(screen.getByRole("alert")).toHaveTextContent("operations[0].inputId");
  expect(screen.queryByText("acquisition.records.futureAudit")).toBeNull();
  expect(request).toHaveBeenCalledWith(7,8,{attemptId:"8/1"},expect.objectContaining({sensitiveResponse:true}));
});

it("labels irrecoverable legacy structure and does not invent old response values", async () => {
  request.mockResolvedValue({code: 0, data: { calls: [{inputKind:"recordedFiles", inputPreview:"a.zip", inputTruncated:false,
    outputKind:"structure", outputPreview:'{"reason":"?"}', outputTruncated:true}]}});
  render(<AssessmentInputOutput taskId={7} attempt={attempt} />);
  expect(await screen.findByText('{"reason":"?"}')).toBeVisible();
  expect(screen.getByText("acquisition.records.structureHint")).toBeVisible();
  expect(screen.queryByRole("alert")).toBeNull();
});
