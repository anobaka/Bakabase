import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Accordion, AccordionItem, Button } from "@/components/bakaui";
import ExternalLink from "@/components/ExternalLink";
import { copyTextToClipboard } from "@/core/clipboard";
import SourceHealthBadge from "@/components/SourceAvailability/SourceHealthBadge";
import type { SourceHealth } from "@/components/SourceAvailability/models";

export interface AccessCodeRevision {
  correctionId: string;
  expectedSourceIdentity: string;
  previousAccessCode?: string | null;
  newAccessCode?: string | null;
  basis: string;
  evidenceReference?: string | null;
  evidence?: string | null;
  appliedAt: string;
}

interface SourceDetails {
  url: string;
  originalUrl?: string | null;
  accessCode?: string | null;
  archivePassword?: string | null;
  accessCodeRevisions?: AccessCodeRevision[] | null;
}

/** The persisted source, shown without resolving it or retrying any remote operation. */
export default function AcquisitionSourceDetails({
  source,
  health,
  disabled,
}: {
  source: SourceDetails;
  health?: SourceHealth | null;
  disabled?: boolean;
}) {
  const { t } = useTranslation();
  const [copied, setCopied] = useState<string>();
  const [copyFailed, setCopyFailed] = useState(false);
  const revisions = source.accessCodeRevisions ?? [];
  const isWebUrl = (value: string) => {
    try {
      return ["http:", "https:"].includes(new URL(value).protocol);
    } catch {
      return false;
    }
  };
  const copy = async (value: string) => {
    setCopyFailed(false);
    try {
      await copyTextToClipboard(value);
      setCopied(value);
    } catch {
      setCopied(undefined);
      setCopyFailed(true);
    }
  };
  const fields = [
    { key: "url", value: source.url },
    ...(source.originalUrl && source.originalUrl !== source.url
      ? [{ key: "originalUrl", value: source.originalUrl }]
      : []),
    { key: "accessCode", value: source.accessCode },
    ...(source.archivePassword ? [{ key: "archivePassword", value: source.archivePassword }] : []),
  ];
  return (
    <div className="min-w-0 space-y-1">
      {(health || disabled) && <SourceHealthBadge health={health} disabled={disabled} />}
      <dl className="space-y-1 text-xs">
        {fields.map(({ key, value }) => (
          <div key={key} className="flex min-w-0 flex-wrap items-start gap-x-2 gap-y-1">
            <dt className="pt-1 text-default-500">
              {t(
                key === "accessCode" && revisions.length
                  ? "acquisition.sourceDetails.currentAccessCode"
                  : `acquisition.sourceDetails.${key}`,
              )}
            </dt>
            <dd className="flex min-w-0 flex-1 items-start gap-2">
              <div className="min-w-0 flex-1 break-all pt-1">
                {(key === "url" || key === "originalUrl") && value && isWebUrl(value) ? (
                  <ExternalLink href={value} className="inline break-all text-xs">
                    {value}
                  </ExternalLink>
                ) : (
                  <span className="select-text whitespace-pre-wrap">
                    {value || t("acquisition.sourceDetails.notProvided")}
                  </span>
                )}
              </div>
              {!!value && (
                <Button
                  size="sm"
                  variant="light"
                  className="h-6 min-w-0 shrink-0 px-2"
                  aria-label={t(`acquisition.sourceDetails.copy.${key}`)}
                  onPress={() => void copy(value)}
                >
                  {t(copied === value ? "common.state.copied" : "acquisition.sourceDetails.copy")}
                </Button>
              )}
            </dd>
          </div>
        ))}
      </dl>
      {revisions.length > 0 && (
        <Accordion isCompact className="px-0">
          <AccordionItem
            key="access-code-revisions"
            title={t("acquisition.sourceDetails.revisions.title", { count: revisions.length })}
            classNames={{ title: "text-xs", trigger: "py-2", content: "pb-2 pt-0" }}
          >
            <p className="mb-2 text-xs text-default-500">
              {t("acquisition.sourceDetails.revisions.executionOnly")}
            </p>
            <ol className="space-y-2 text-xs">
              {[...revisions].reverse().map((revision) => (
                <li key={revision.correctionId} className="space-y-2 rounded-lg bg-default-50 p-2">
                  <div className="flex flex-wrap items-center justify-between gap-2 text-default-500">
                    <span>
                      {t(
                        `acquisition.sourceDetails.revisions.basis.${["manual", "sourceEvidence"].includes(revision.basis) ? revision.basis : "unknown"}`,
                      )}
                    </span>
                    <time dateTime={revision.appliedAt}>
                      {Number.isNaN(Date.parse(revision.appliedAt))
                        ? revision.appliedAt
                        : new Date(revision.appliedAt).toLocaleString()}
                    </time>
                  </div>
                  <dl className="space-y-1">
                    <div className="flex flex-wrap gap-x-2">
                      <dt className="text-default-500">
                        {t("acquisition.sourceDetails.revisions.previous")}
                      </dt>
                      <dd className="select-text break-all">
                        {revision.previousAccessCode ||
                          t("acquisition.sourceDetails.revisions.notSet")}
                      </dd>
                    </div>
                    <div className="flex flex-wrap gap-x-2">
                      <dt className="text-default-500">
                        {t("acquisition.sourceDetails.revisions.updated")}
                      </dt>
                      <dd className="select-text break-all">
                        {revision.newAccessCode || t("acquisition.sourceDetails.revisions.removed")}
                      </dd>
                    </div>
                  </dl>
                  {(revision.evidence || revision.evidenceReference) && (
                    <div className="space-y-1">
                      <p className="text-default-500">
                        {t("acquisition.sourceDetails.revisions.evidence")}
                      </p>
                      {revision.evidenceReference &&
                        (isWebUrl(revision.evidenceReference) ? (
                          <ExternalLink
                            href={revision.evidenceReference}
                            className="inline break-all text-xs"
                          >
                            {revision.evidenceReference}
                          </ExternalLink>
                        ) : (
                          <p className="break-all">{revision.evidenceReference}</p>
                        ))}
                      {revision.evidence && (
                        <p className="whitespace-pre-wrap break-words">{revision.evidence}</p>
                      )}
                    </div>
                  )}
                </li>
              ))}
            </ol>
          </AccordionItem>
        </Accordion>
      )}
      {copyFailed && (
        <p role="alert" className="text-xs text-danger">
          {t("common.message.copyFailed")}
        </p>
      )}
    </div>
  );
}
