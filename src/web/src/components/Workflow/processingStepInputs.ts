/** Explicit multiple inputs replace the legacy single input, including invalid empty arrays. */
export function processingStepInputs(step: {
  input?: string | null;
  inputs?: readonly string[] | null;
}): readonly string[] {
  return step.inputs ?? [step.input ?? "download"];
}
