import { beforeEach, expect, it, vi } from "vitest";
import { startAcquisitionDiagnosis } from "../components/acquisitionDiagnosis";
const api = vi.hoisted(() => ({ preview: vi.fn(), cancelDiagnosis: vi.fn() }));
vi.mock("../components/acquisitionRecoveryApi", () => ({ acquisitionRecoveryApi: api }));
beforeEach(() => { vi.clearAllMocks(); api.preview.mockReset().mockImplementation(() => new Promise(() => {})); api.cancelDiagnosis.mockReset(); });

it("aborts transport immediately but resolves stop only after a matching server acknowledgement", async () => {
  let acknowledge!: (value: unknown) => void;
  api.cancelDiagnosis.mockReturnValue(new Promise(resolve => { acknowledge = resolve; }));
  const diagnosis = startAcquisitionDiagnosis([8, 2], "downloads");
  const options = api.preview.mock.calls[0][2];
  expect(options.diagnosisId).toMatch(/^[0-9a-f]{32}$/);
  expect(api.preview).toHaveBeenCalledWith([8, 2], "downloads", options);
  const stopped = vi.fn();
  const first = diagnosis.stop();
  const second = diagnosis.stop();
  void first.then(stopped);
  expect(first).toBe(second);
  expect(options.signal.aborted).toBe(true);
  expect(api.cancelDiagnosis).toHaveBeenCalledExactlyOnceWith(options.diagnosisId);
  await Promise.resolve();
  expect(stopped).not.toHaveBeenCalled();
  acknowledge({ diagnosisId: options.diagnosisId, status: "stopped" });
  await first;
  expect(stopped).toHaveBeenCalledTimes(1);
  expect(api.preview).toHaveBeenCalledTimes(1);
});

it.each([{ status: "pending" }, { diagnosisId: "other", status: "stopped" }])(
  "rejects an unconfirmed stop and permits retrying cancellation, never restarting diagnosis (%j)", async invalid => {
    const diagnosis = startAcquisitionDiagnosis([1], "workflow");
    const id = api.preview.mock.calls[0][2].diagnosisId;
    api.cancelDiagnosis.mockResolvedValueOnce({ diagnosisId: id, ...invalid });
    await expect(diagnosis.stop()).rejects.toThrow("RECOVERY_DIAGNOSIS_STOP_UNCONFIRMED");
    api.cancelDiagnosis.mockResolvedValueOnce({ diagnosisId: id, status: "stopped" });
    await diagnosis.stop();
    expect(api.cancelDiagnosis.mock.calls.map(call => call[0])).toEqual([id, id]);
    expect(api.preview).toHaveBeenCalledTimes(1);
  },
);

it("does not require another server request to stop an already completed diagnosis", async () => {
  api.preview.mockResolvedValueOnce({ rows: [] });
  api.cancelDiagnosis.mockRejectedValue(new Error("server unavailable"));
  const diagnosis = startAcquisitionDiagnosis([1], "workflow");
  await diagnosis.request;
  await expect(diagnosis.stop()).resolves.toBeUndefined();
  await expect(diagnosis.stop()).resolves.toBeUndefined();
  expect(api.cancelDiagnosis).not.toHaveBeenCalled();
  expect(api.preview).toHaveBeenCalledTimes(1);
});

it("still requires server acknowledgement after a rejected diagnosis response", async () => {
  api.preview.mockRejectedValueOnce(new Error("connection lost"));
  const diagnosis = startAcquisitionDiagnosis([1], "workflow");
  const options = api.preview.mock.calls[0][2];
  await expect(diagnosis.request).rejects.toThrow("connection lost");
  api.cancelDiagnosis.mockResolvedValueOnce({ diagnosisId: options.diagnosisId, status: "stopped" });
  await diagnosis.stop();
  expect(options.signal.aborted).toBe(true);
  expect(api.cancelDiagnosis).toHaveBeenCalledExactlyOnceWith(options.diagnosisId);
});

it("keeps an in-flight cancellation acknowledgement authoritative if the diagnosis then completes", async () => {
  let complete!: (value: unknown) => void;
  let acknowledge!: (value: unknown) => void;
  api.preview.mockReturnValueOnce(new Promise(resolve => { complete = resolve; }));
  api.cancelDiagnosis.mockReturnValueOnce(new Promise(resolve => { acknowledge = resolve; }));
  const diagnosis = startAcquisitionDiagnosis([1], "workflow");
  const id = api.preview.mock.calls[0][2].diagnosisId;
  const first = diagnosis.stop();
  const stopped = vi.fn();
  void first.then(stopped);
  complete({ rows: [] });
  await diagnosis.request;
  expect(diagnosis.stop()).toBe(first);
  expect(stopped).not.toHaveBeenCalled();
  acknowledge({ diagnosisId: id, status: "stopped" });
  await first;
  expect(stopped).toHaveBeenCalledTimes(1);
  expect(api.cancelDiagnosis).toHaveBeenCalledExactlyOnceWith(id);
});
