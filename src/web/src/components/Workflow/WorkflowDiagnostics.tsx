import type { WorkflowValidation } from "./metadata";

import React from "react";
import { useTranslation } from "react-i18next";
import {
  CheckCircleOutlined,
  LoadingOutlined,
  QuestionCircleOutlined,
  WarningOutlined,
} from "@ant-design/icons";

import { activityDisplayName, triggerDisplayName } from "./displayNames";

import { Button } from "@/components/bakaui";

type Props = {
  result?: WorkflowValidation | null;
  loading?: boolean;
  failed?: boolean;
  onCheck?: () => void;
  onSelectNode?: (index: number) => void;
};

export const hasWorkflowConfigurationErrors = (result?: WorkflowValidation | null) =>
  !!result?.diagnostics.some(
    (diagnostic) => diagnostic.severity === "error" && !diagnostic.dependsOnPayload,
  );

/** The same configuration diagnostics are used in the editor and workflow consumers. */
const WorkflowDiagnostics = ({ result, loading, failed, onCheck, onSelectNode }: Props) => {
  const { t } = useTranslation();

  const status = loading
    ? "checking"
    : failed
      ? "failed"
      : result
        ? result.isValid
          ? "passed"
          : hasWorkflowConfigurationErrors(result)
            ? "needsAttention"
            : "needsInput"
        : "unchecked";
  const hasDiagnostics = !loading && !failed && !!result?.diagnostics.length;
  const statusLine = (
    <span
      className={`inline-flex items-center gap-1.5 ${failed || result?.isValid === false ? "text-warning-600" : "text-default-500"}`}
    >
      {loading ? (
        <LoadingOutlined aria-hidden spin />
      ) : failed || result?.isValid === false ? (
        <WarningOutlined aria-hidden />
      ) : result ? (
        <CheckCircleOutlined aria-hidden />
      ) : (
        <QuestionCircleOutlined aria-hidden />
      )}
      <span>{t<string>(`workflow.diagnostics.${status}`)}</span>
      {hasDiagnostics && <span className="text-default-400">({result!.diagnostics.length})</span>}
    </span>
  );

  return (
    <section aria-live="polite" className="text-xs">
      {hasDiagnostics ? (
        <details className="group">
          <summary className="w-fit cursor-pointer py-1 marker:text-default-400">
            {statusLine}
          </summary>
          <ul className="mt-2 space-y-2">
            {result!.diagnostics.map((diagnostic, index) => (
              <li
                key={`${diagnostic.nodeId ?? diagnostic.nodeIndex ?? "workflow"}-${diagnostic.code}-${index}`}
                className="rounded-lg bg-default-100 p-2 leading-relaxed"
              >
                <div className="flex flex-wrap items-center gap-1.5 font-medium">
                  <span
                    className={diagnostic.severity === "error" ? "text-danger" : "text-warning-600"}
                  >
                    {t<string>(`workflow.diagnostics.severity.${diagnostic.severity}`)}
                  </span>
                  {diagnostic.nodeIndex != null &&
                    (onSelectNode ? (
                      <Button
                        className="h-auto min-w-0 px-1 py-0.5 text-xs"
                        size="sm"
                        variant="light"
                        onPress={() => onSelectNode(diagnostic.nodeIndex!)}
                      >
                        {t<string>("workflow.diagnostics.node", {
                          index: diagnostic.nodeIndex + 1,
                        })}
                      </Button>
                    ) : (
                      <span>
                        {t<string>("workflow.diagnostics.node", {
                          index: diagnostic.nodeIndex + 1,
                        })}
                      </span>
                    ))}
                  {diagnostic.kind && (
                    <span>
                      {diagnostic.nodeIndex == null
                        ? triggerDisplayName(t, diagnostic.kind)
                        : activityDisplayName(t, diagnostic.kind)}
                    </span>
                  )}
                </div>
                <p className="mt-1 break-words text-default-600">
                  {diagnostic.messageKey
                    ? t<string>(diagnostic.messageKey, { defaultValue: diagnostic.message })
                    : diagnostic.message}
                </p>
              </li>
            ))}
          </ul>
          <p className="mt-2 leading-relaxed text-default-500">
            {t<string>("workflow.diagnostics.limit")}
          </p>
        </details>
      ) : (
        <div
          className="flex min-h-6 flex-wrap items-center gap-2"
          title={result?.isValid ? t<string>("workflow.diagnostics.limit") : undefined}
        >
          {statusLine}
          {failed && onCheck && (
            <Button
              className="h-6 min-w-0 px-2 text-xs"
              size="sm"
              variant="light"
              onPress={onCheck}
            >
              {t<string>("workflow.diagnostics.retry")}
            </Button>
          )}
        </div>
      )}
    </section>
  );
};

export default WorkflowDiagnostics;
