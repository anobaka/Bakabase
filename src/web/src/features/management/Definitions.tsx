import type { IProperty } from "@/components/Property/models";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";

import { ownerClient, type ManagementSource } from "./api";

import { Button, Input, Modal, Spinner } from "@/components/bakaui";
import PropertyModal from "@/components/PropertyModal";
import PropertyTypeIcon from "@/components/Property/components/PropertyTypeIcon";
import { PropertyPool } from "@/sdk/constants";

export interface Library {
  id: number;
  name: string;
  color?: string;
  paths?: string[];
  resourceCount?: number;
}
export function DefinitionGroup({
  source,
  kind,
  keyword,
}: {
  source: ManagementSource;
  kind: "properties" | "libraries";
  keyword: string;
}) {
  const { t } = useTranslation();
  const request = useMemo(() => ownerClient(source), [source]);
  const [items, setItems] = useState<(IProperty | Library)[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string>();
  const [propertyEdit, setPropertyEdit] = useState<IProperty | "new">();
  const [libraryEdit, setLibraryEdit] = useState<Partial<Library>>();
  const [deleting, setDeleting] = useState<IProperty | Library>();
  const load = useCallback(async () => {
    if (!source.available) return;
    setLoading(true);
    setError(undefined);
    try {
      setItems(
        (
          await request<(IProperty | Library)[]>(
            kind === "properties" ? "customProperties" : "libraries",
          )
        ).data ?? [],
      );
    } catch (reason) {
      setError(String(reason));
    } finally {
      setLoading(false);
    }
  }, [source, request, kind]);

  useEffect(() => {
    void load();
  }, [load]);
  if (!source.available)
    return (
      <section className="rounded-lg border border-warning/30 p-4">
        <h3 className="font-medium">{source.name}</h3>
        <p className="text-sm text-warning">
          {t("management.unavailable")} {source.error}
        </p>
      </section>
    );
  const filtered = items.filter((item) =>
    item.name.toLocaleLowerCase().includes(keyword.toLocaleLowerCase()),
  );

  return (
    <section className="rounded-lg border border-divider p-4">
      <header className="mb-3 flex items-center justify-between gap-3">
        <h3 className="font-medium">
          {source.name}{" "}
          <span className="text-xs text-default-500">
            {source.isLocal ? t("management.thisDevice") : t("management.owner")}
          </span>
        </h3>
        <Button
          size="sm"
          onPress={() =>
            kind === "properties" ? setPropertyEdit("new") : setLibraryEdit({ name: "", paths: [] })
          }
        >
          {t("management.create")}
        </Button>
      </header>
      {loading && <Spinner size="sm" />}
      {error ? (
        <div className="text-sm text-danger" role="alert">
          {error}{" "}
          <Button size="sm" onPress={load}>
            {t("management.retry")}
          </Button>
        </div>
      ) : !loading && !filtered.length ? (
        <p className="py-6 text-center text-sm text-default-500">{t("management.empty")}</p>
      ) : (
        <div className="divide-y divide-divider">
          {filtered.map((item) => (
            <div key={item.id} className="flex min-w-0 items-center gap-3 py-2">
              {kind === "properties" && (
                <PropertyTypeIcon textVariant="none" type={(item as IProperty).type} />
              )}
              <div className="min-w-0 flex-1">
                <div className="truncate text-sm font-medium">{item.name}</div>
                {kind === "libraries" && (
                  <div className="text-xs text-default-500">
                    {t("management.resourceCount", { count: (item as Library).resourceCount ?? 0 })}
                  </div>
                )}
              </div>
              <Button
                size="sm"
                variant="light"
                onPress={() =>
                  kind === "properties"
                    ? setPropertyEdit(item as IProperty)
                    : setLibraryEdit({ ...item })
                }
              >
                {t("management.edit")}
              </Button>
              <Button color="danger" size="sm" variant="light" onPress={() => setDeleting(item)}>
                {t("management.delete")}
              </Button>
            </div>
          ))}
        </div>
      )}
      {propertyEdit && (
        <PropertyModal
          checkOptionUsage={async (id, optionId) => {
            const result = await request<{ isReady: boolean; counts: Record<string, number> }>(
              "propertyValueResourceCounts",
              {
                id,
                query: { pool: [String(PropertyPool.Custom)] },
                body: { page: 1, pageSize: 100 },
              },
            );

            if (!result.data?.isReady || !result.data.counts)
              throw new Error(t("property.editor.usageUnavailable"));

            return result.data.counts[optionId] ?? 0;
          }}
          ownerScoped={!source.isLocal}
          saveProperty={async (body) => {
            const result = await request<IProperty>(
              body.id ? "customPropertyPut" : "customPropertyAdd",
              { id: body.id, body },
            );

            return result.data!;
          }}
          value={propertyEdit === "new" ? undefined : propertyEdit}
          onDestroyed={() => setPropertyEdit(undefined)}
          onSaved={() => {
            setPropertyEdit(undefined);
            void load();
          }}
        />
      )}
      {libraryEdit && (
        <Modal
          visible
          footer={{ actions: ["ok", "cancel"], okProps: { isDisabled: !libraryEdit.name?.trim() } }}
          title={t("management.libraryEditor", { device: source.name })}
          onClose={() => setLibraryEdit(undefined)}
          onOk={async () => {
            const body = {
              name: libraryEdit.name!.trim(),
              color: libraryEdit.color,
              paths: (libraryEdit.paths ?? []).filter((path) => path.trim().length > 0),
            };

            await request(libraryEdit.id ? "libraryPatch" : "libraryAdd", {
              id: libraryEdit.id,
              body,
            });
            setLibraryEdit(undefined);
            await load();
          }}
        >
          <div className="flex flex-col gap-4">
            <Input
              label={t("management.name")}
              value={libraryEdit.name ?? ""}
              onValueChange={(name) => setLibraryEdit({ ...libraryEdit, name })}
            />
            <Input
              label={t("management.color")}
              value={libraryEdit.color ?? ""}
              onValueChange={(color) => setLibraryEdit({ ...libraryEdit, color })}
            />
            <label className="flex flex-col gap-1 text-sm">
              {t("management.libraryPaths")}
              <textarea
                className="min-h-24 rounded-lg border border-divider bg-content2 p-2 font-mono text-xs"
                value={(libraryEdit.paths ?? []).join("\n")}
                onChange={(e) =>
                  setLibraryEdit({
                    ...libraryEdit,
                    paths: e.target.value.split("\n"),
                  })
                }
              />
            </label>
            <p className="text-xs text-default-500">
              {t("management.ownerPathHelp", { device: source.name })}
            </p>
          </div>
        </Modal>
      )}
      {deleting && (
        <Modal
          visible
          footer={{ actions: ["ok", "cancel"], okProps: { color: "danger" } }}
          title={t("management.deleteTitle", { name: deleting.name })}
          onClose={() => setDeleting(undefined)}
          onOk={async () => {
            await request(kind === "properties" ? "customPropertyDelete" : "libraryDelete", {
              id: deleting.id,
            });
            setDeleting(undefined);
            await load();
          }}
        >
          <p>{t("management.deleteHelp", { device: source.name })}</p>
        </Modal>
      )}
    </section>
  );
}
