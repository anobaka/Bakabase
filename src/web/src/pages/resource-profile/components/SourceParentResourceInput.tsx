import type { SourceScope } from "../sourceProfiles";
import type { Props as ValueProps } from "@/components/Property/components/PropertyValueRenderer";
import type { Resource } from "@/core/models/Resource";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import { profileSourceApi } from "../sourceProfiles";

import { Button, Input, Modal, Pagination, Spinner } from "@/components/bakaui";
import { deserializeStandardValue, serializeStandardValue } from "@/components/StandardValue";
import { getDbValueType, getBizValueType } from "@/components/Property/PropertySystem";
import { ResourceTag, StandardValueType } from "@/sdk/constants";

/** Parent IDs are chosen from the profile's source, never from this device's resource table. */
export default function SourceParentResourceInput({
  scope,
  ...props
}: ValueProps & { scope: SourceScope }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [keyword, setKeyword] = useState("");
  const [page, setPage] = useState(1);
  const [result, setResult] = useState<{ resources: Resource[]; total: number }>();
  const [error, setError] = useState(false);
  const type = getDbValueType(props.property.type);
  const multiple = type === StandardValueType.ListString;
  const raw = deserializeStandardValue(props.dbValue ?? "", type);
  const names = deserializeStandardValue(
    props.bizValue ?? "",
    getBizValueType(props.property.type),
  );
  const ids = (Array.isArray(raw) ? raw : raw ? [raw] : []).map(String);
  const [selected, setSelected] = useState<Record<string, string>>({});

  useEffect(() => {
    if (!open) return;
    const controller = new AbortController();

    setResult(undefined);
    setError(false);
    void profileSourceApi
      .preview(
        scope,
        { page, pageSize: 25, keyword, tags: [ResourceTag.IsParent] },
        controller.signal,
      )
      .then((response) => {
        if (!controller.signal.aborted)
          setResult({ resources: response.data ?? [], total: response.totalCount ?? 0 });
      })
      .catch(() => {
        if (!controller.signal.aborted) setError(true);
      });

    return () => controller.abort();
  }, [open, keyword, page, scope.sourceDeviceId, scope.sourceLibraryEpoch]);

  return (
    <>
      <Button
        isDisabled={props.isReadonly}
        size={props.size}
        variant="light"
        onPress={() => {
          const labels = Array.isArray(names) ? names : names ? [names] : [];

          setSelected(
            Object.fromEntries(ids.map((id, index) => [id, String(labels[index] ?? `#${id}`)])),
          );
          setOpen(true);
        }}
      >
        {ids.length
          ? Array.isArray(names)
            ? names.join(", ")
            : names || ids.join(", ")
          : t("common.action.select")}
      </Button>
      {open && (
        <Modal
          defaultVisible
          size="2xl"
          title={t("common.label.selectParentResources")}
          onDestroyed={() => setOpen(false)}
          onOk={() => {
            const entries = Object.entries(selected);

            props.onValueChange?.(
              entries.length
                ? serializeStandardValue(multiple ? entries.map(([id]) => id) : entries[0][0], type)
                : undefined,
              entries.length
                ? serializeStandardValue(
                    multiple ? entries.map(([, name]) => name) : entries[0][1],
                    getBizValueType(props.property.type),
                  )
                : undefined,
            );
            setOpen(false);
          }}
        >
          <Input
            aria-label={t("resourceProfile.source.searchResources")}
            placeholder={t("resourceProfile.source.searchResources")}
            value={keyword}
            onValueChange={(value) => {
              setKeyword(value);
              setPage(1);
            }}
          />
          {error ? (
            <p className="text-sm text-danger" role="alert">
              {t("resourceProfile.source.unavailable")}
            </p>
          ) : !result ? (
            <Spinner />
          ) : (
            <>
              <div className="max-h-80 overflow-y-auto">
                {result.resources.map((resource) => {
                  const id = String(resource.id);
                  const name =
                    resource.displayName || resource.path?.split(/[\\/]/).pop() || `#${id}`;

                  return (
                    <Button
                      key={id}
                      aria-pressed={id in selected}
                      className="w-full justify-start"
                      color={id in selected ? "primary" : "default"}
                      variant="light"
                      onPress={() =>
                        setSelected((previous) => {
                          if (id in previous) {
                            const next = { ...previous };

                            delete next[id];

                            return next;
                          }

                          return multiple ? { ...previous, [id]: name } : { [id]: name };
                        })
                      }
                    >
                      {name}
                    </Button>
                  );
                })}
              </div>
              {result.total > 25 && (
                <Pagination page={page} total={Math.ceil(result.total / 25)} onChange={setPage} />
              )}
            </>
          )}
        </Modal>
      )}
    </>
  );
}
