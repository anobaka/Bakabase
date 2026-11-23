import { lazy, Suspense, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button } from "@/components/bakaui";
import ExternalLink from "@/components/ExternalLink";
import { browserExternalUrl } from "@/utils/openExternalUrl";
import type { PlanTask } from "./PlanTask/models";

type CreationOrigin = NonNullable<PlanTask["creationOrigins"]>[number];

const PostOriginModal = lazy(() => import("./PostOriginModal"));

export const postTaskId = (origin: CreationOrigin) => {
  if (origin.kind !== "postParser" || origin.entityKind !== "task") return undefined;
  const raw = origin.entityId ?? "";
  if (!/^[1-9]\d*$/.test(raw)) return undefined;
  const id = Number(raw);
  return Number.isSafeInteger(id) && id <= 2147483647 ? id : undefined;
};
/** Provenance is supplied by the server; download hosts never imply a creator. */
export default function AcquisitionCreationOrigin({
  origins,
  compact = false,
}: {
  origins?: PlanTask["creationOrigins"];
  compact?: boolean;
}) {
  const { t } = useTranslation();
  const [selected, setSelected] = useState<{ id: number; revision?: string | null; href?: string }>();
  if (!origins?.length) return null;
  return (
    <section
      aria-label={t("acquisition.origin.label")}
      className={compact ? "space-y-1 text-xs" : "space-y-2 rounded-lg bg-default-50 p-3 text-sm"}
    >
      {origins.map((origin, index) => {
        const id = postTaskId(origin);
        const href = origin.url ? browserExternalUrl(origin.url) : undefined;
        const available = origin.sourceState === "available";
        const label = id
          ? t("acquisition.origin.postTask", { id })
          : origin.kind === "nameSearch" && origin.builderId === "exhentai.search"
            ? t("acquisition.plan.builder.exhentai.name")
            : t(`acquisition.origin.kind.${origin.kind}`, {
                defaultValue: t("acquisition.origin.kind.unknown"),
              });
        return (
          <div
            key={JSON.stringify([
              origin.module,
              origin.entityKind,
              origin.entityId,
              origin.revision,
              index,
            ])}
            className="min-w-0 space-y-1"
          >
            <div className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
              <span className="text-xs text-default-500">{t("acquisition.origin.label")}</span>
              {id && available ? (
                <Button size="sm" variant="light" color="primary" aria-haspopup="dialog"
                  className="h-6 min-w-0 px-1 text-xs" onPress={() => setSelected({id, revision: origin.revision, href})}>
                  {label}
                </Button>
              ) : (
                <span className="text-xs">{label}</span>
              )}
              {!compact && origin.revision && (
                <span className="text-xs text-default-500">
                  {t("acquisition.origin.revision", { revision: origin.revision })}
                </span>
              )}
              {href && (
                <ExternalLink href={href} className="text-xs">
                  {t(
                    origin.kind === "postParser"
                      ? "acquisition.origin.openPost"
                      : "acquisition.origin.openSource",
                  )}
                </ExternalLink>
              )}
              {["deleted", "missing", "snapshot"].includes(origin.sourceState) && (
                <span className="text-xs text-default-500">
                  {t(`acquisition.origin.state.${origin.sourceState}`)}
                </span>
              )}
            </div>
            {!compact && origin.title && (
              <p className="break-words text-default-600">{origin.title}</p>
            )}
          </div>
        );
      })}
      {selected && (
        <Suspense fallback={null}>
          <PostOriginModal key={selected.id} {...selected} onClose={() => setSelected(undefined)} />
        </Suspense>
      )}
    </section>
  );
}
