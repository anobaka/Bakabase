import type { WorkflowValidation } from "./metadata";

import React from "react";
import { useTranslation } from "react-i18next";

import { activityDisplayName, triggerDisplayName } from "./displayNames";

import { Button } from "@/components/bakaui";

type Props = {
  result?: WorkflowValidation | null;
  loading?: boolean;
  failed?: boolean;
  onCheck?: () => void;
  onSelectNode?: (index: number) => void;
};

/** The same configuration diagnostics are used in the editor and workflow consumers. */
const WorkflowDiagnostics = ({ result, loading, failed, onCheck, onSelectNode }: Props) => {
  const { t } = useTranslation();

  return (
    <section aria-live="polite" className="flex flex-col gap-2 text-xs">
      <div className="flex flex-wrap items-center gap-2">
        <span className="font-medium text-default-700">
          {t<string>("workflow.diagnostics.title")}
        </span>
        {onCheck && (
          <Button isLoading={loading} size="sm" variant="light" onPress={onCheck}>
            {t<string>(failed ? "workflow.diagnostics.retry" : "workflow.diagnostics.check")}
          </Button>
        )}
      </div>
      {failed ? (
        <p className="text-warning-600">{t<string>("workflow.diagnostics.failed")}</p>
      ) : loading ? (
        <p className="text-default-500">{t<string>("workflow.diagnostics.checking")}</p>
      ) : result ? (
        <>
          <p className={result.isValid ? "text-default-600" : "text-warning-600"}>
            {t<string>(
              result.isValid
                ? "workflow.diagnostics.passed"
                : "workflow.diagnostics.needsAttention",
            )}
          </p>
          {result.diagnostics.length > 0 && (
            <ul className="space-y-2">
              {result.diagnostics.map((diagnostic, index) => (
                <li
                  key={`${diagnostic.nodeId ?? diagnostic.nodeIndex ?? "workflow"}-${diagnostic.code}-${index}`}
                  className="rounded-lg bg-default-100 p-2 leading-relaxed"
                >
                  <div className="flex flex-wrap items-center gap-1.5 font-medium">
                    <span
                      className={
                        diagnostic.severity === "error" ? "text-danger" : "text-warning-600"
                      }
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
          )}
          <p className="text-default-500">{t<string>("workflow.diagnostics.limit")}</p>
        </>
      ) : (
        <p className="text-default-500">{t<string>("workflow.diagnostics.unchecked")}</p>
      )}
    </section>
  );
};

export default WorkflowDiagnostics;
