import { useTranslation } from "react-i18next";

const conflictKinds = new Set([
  "PlannedTargetCollision",
  "FileDirectoryConflict",
  "PathAliasCollision",
  "ExistingTargetConflict",
]);

/** The diagnostic is persisted in a waiting prompt; tolerate incomplete historical payloads. */
export default function ProcessingPathConflictSummary({ conflict }: { conflict: unknown }) {
  const { t } = useTranslation();
  if (!conflict || typeof conflict !== "object" || Array.isArray(conflict)) return null;
  const value = conflict as Record<string, unknown>;
  const entries = Array.isArray(value.entries)
    ? value.entries.filter(
        (entry): entry is { sourcePath: string; targetPath: string } =>
          !!entry &&
          typeof entry === "object" &&
          typeof entry.sourcePath === "string" &&
          typeof entry.targetPath === "string",
      )
    : [];
  return (
    <div className="min-w-0 space-y-2 text-sm" role="status">
      <p className="font-medium text-warning-600">
        {t(
          `workflow.fileRecovery.pathConflict.kind.${
            typeof value.kind === "string" && conflictKinds.has(value.kind) ? value.kind : "unknown"
          }`,
        )}
      </p>
      {typeof value.stepId === "string" && (
        <p className="break-all">
          {t("workflow.fileRecovery.pathConflict.step", { id: value.stepId })}
        </p>
      )}
      {typeof value.targetPath === "string" && (
        <p className="break-all">
          {t("workflow.fileRecovery.pathConflict.target", { path: value.targetPath })}
        </p>
      )}
      {entries.length > 0 && (
        <div className="space-y-1">
          <p className="text-xs text-default-500">
            {t("workflow.fileRecovery.pathConflict.mappings")}
          </p>
          <ul className="max-h-48 space-y-1 overflow-y-auto text-xs">
            {entries.map((entry, index) => (
              <li key={`${entry.sourcePath}:${entry.targetPath}:${index}`} className="break-all">
                <span>{entry.sourcePath}</span>
                {" → "}
                <span>{entry.targetPath}</span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}
