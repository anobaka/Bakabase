import { useTranslation } from "react-i18next";
import type { AcquisitionLocalFilesRecoveryPreview as Preview } from "./acquisitionRecoveryApi";

/** A confirmation must include the local input and the exact replacement directories for every part. */
export function hasLocalFilesRecoveryEvidence(preview?: Preview | null): boolean {
  const parts = preview?.parts;
  const text = (value: unknown): value is string => typeof value === "string" && !!value.trim();
  return (
    Array.isArray(parts) &&
    parts.length > 0 &&
    parts.every(
      (part) =>
        !!part &&
        text(part.partId) &&
        text(part.sourceDirectory) &&
        text(part.workingDirectory) &&
        text(part.deliveryDirectory) &&
        text(part.fingerprint) &&
        Number.isSafeInteger(part.downloadTaskId) &&
        part.downloadTaskId > 0 &&
        Number.isSafeInteger(part.downloadResultId) &&
        part.downloadResultId > 0 &&
        Number.isSafeInteger(part.fileCount) &&
        part.fileCount > 0,
    ) &&
    new Set(parts.map((part) => part.partId)).size === parts.length
  );
}

export default function AcquisitionLocalFilesRecoveryPreview({ preview }: { preview: Preview }) {
  const { t } = useTranslation();
  return (
    <div className="max-h-72 space-y-2 overflow-y-auto py-1 text-xs">
      {preview.parts
        .filter((part) => !!part && typeof part === "object")
        .map((part, index) => (
          <section key={`${part.partId}:${index}`} className="min-w-0 space-y-1.5">
            <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
              <span className="break-all font-medium">
                {t("acquisition.recovery.localFiles.part")} {part.partId}
              </span>
              <span className="text-default-500">
                {t("acquisition.recovery.localFiles.evidence", {
                  id: part.downloadTaskId,
                  count: part.fileCount,
                })}
              </span>
            </div>
            <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-3 gap-y-1">
              <dt className="text-default-500">
                {t("acquisition.recovery.localFiles.sourceDirectory")}
              </dt>
              <dd className="break-all">{part.sourceDirectory}</dd>
              {(["workingDirectory", "deliveryDirectory"] as const).map((field) => {
                const old =
                  field === "workingDirectory"
                    ? part.originalWorkingDirectory
                    : part.originalDeliveryDirectory;
                return (
                  <div key={field} className="contents">
                    <dt className="text-default-500">
                      {t(`acquisition.recovery.localFiles.${field}`)}
                    </dt>
                    <dd className="min-w-0 space-y-0.5 break-all">
                      <p className="text-default-500">
                        {t("acquisition.recovery.localFiles.previousDirectory")}
                        {": "}
                        {old || t("acquisition.recovery.localFiles.directoryNotRecorded")}
                      </p>
                      <p>
                        {t("acquisition.recovery.localFiles.currentDirectory")}
                        {": "}
                        {part[field]}
                      </p>
                    </dd>
                  </div>
                );
              })}
            </dl>
          </section>
        ))}
    </div>
  );
}
