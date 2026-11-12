import { acquisitionRecoveryApi, type AcquisitionRecoveryScope } from "./acquisitionRecoveryApi";

/** A server acknowledgement, rather than a discarded HTTP promise, ends a diagnosis. */
export function startAcquisitionDiagnosis(taskIds: number[], scope: AcquisitionRecoveryScope) {
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  const diagnosisId = Array.from(bytes, (byte) => byte.toString(16).padStart(2, "0")).join("");
  const controller = new AbortController();
  let serverAlreadyFinished = false;
  const request = acquisitionRecoveryApi.preview(taskIds, scope, {
    diagnosisId,
    signal: controller.signal,
  }).then((preview) => {
    // A successful response is sent only after the controller's finally has exited.
    // Aborted/rejected transport must never count as this acknowledgement.
    serverAlreadyFinished = true;
    return preview;
  });
  // A modal can unmount before attaching its next render's observer.
  void request.catch(() => {});
  let cancellation: Promise<void> | undefined;
  return {
    scope,
    request,
    stop: () => {
      if (cancellation) return cancellation;
      if (serverAlreadyFinished) return Promise.resolve();
      controller.abort();
      cancellation ??= acquisitionRecoveryApi.cancelDiagnosis(diagnosisId).then((result) => {
        if (result.status !== "stopped" || result.diagnosisId !== diagnosisId)
          throw new Error("RECOVERY_DIAGNOSIS_STOP_UNCONFIRMED");
      }).catch((error: unknown) => {
        cancellation = undefined;
        throw error;
      });
      return cancellation;
    },
  };
}

export type AcquisitionDiagnosis = ReturnType<typeof startAcquisitionDiagnosis>;
