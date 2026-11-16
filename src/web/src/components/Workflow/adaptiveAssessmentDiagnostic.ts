const codes = new Set([
  "LLM_DECISION_INVALID_JSON",
  "LLM_DECISION_UNSUPPORTED_STATUS",
  "LLM_DECISION_INVALID",
  "LLM_ASSESSMENT_FAILED",
  "LLM_ASSESSMENT_TIMEOUT",
  "LLM_OUTPUT_TRUNCATED",
  "MAX_LLM_ASSESSMENT_CALLS",
  "INCOMPLETE_FILE_INVENTORY",
  "ARCHIVE_FAMILY_REVIEW_REQUIRED",
  "ARCHIVE_PACKAGING_REVIEW_REQUIRED",
]);

export function isAdaptiveAssessmentDiagnostic(code?: string): code is string {
  return !!code && codes.has(code);
}

/** Older assessment prompts shared an error code for two different validation failures. */
export function assessmentDiagnosticFromWait(message?: string): string | undefined {
  if (typeof message !== "string") return;
  if (message === "LLM_DECISION_INVALID: 模型返回的处理方案不是有效 JSON。")
    return "LLM_DECISION_INVALID_JSON";
  if (message === "LLM_DECISION_INVALID: 模型未返回受支持的处理状态。")
    return "LLM_DECISION_UNSUPPORTED_STATUS";
  const code = message.split(":", 1)[0];
  return isAdaptiveAssessmentDiagnostic(code) ? code : undefined;
}

/** A model's explanation is display text, never markup or an action to execute. */
export function assessmentExplanationFromWait(prompt: {
  adaptiveAssessment?: unknown;
  reason?: unknown;
  message?: unknown;
}): string | undefined {
  if (prompt.adaptiveAssessment !== true || typeof prompt.reason !== "string" ||
      typeof prompt.message !== "string" || prompt.message.length > 64 * 1024) return;
  const message = prompt.message.trim();
  if (!message || message === prompt.reason || /^[A-Z][A-Z0-9_]*$/.test(message) ||
      assessmentDiagnosticFromWait(message)) return;
  return message;
}
