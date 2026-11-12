import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Checkbox, Modal, Pagination, Spinner, toast } from "@/components/bakaui";
import type { DestroyableProps } from "@/components/bakaui/types";
import BApi from "@/sdk/BApi";

type Page = NonNullable<
  Awaited<ReturnType<typeof BApi.acquisition.searchAcquisitionCandidates>>["data"]
>;

/** The library is queried only after the user explicitly opens this one-time snapshot import. */
export default function ImportLegacyTasksModal({
  onImported,
  onDestroyed,
}: DestroyableProps & { onImported: () => void }) {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  const [retry, setRetry] = useState(0);
  const [data, setData] = useState<Page>();
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [selected, setSelected] = useState<number[]>([]);
  useEffect(() => {
    let active = true;
    setLoading(true);
    setFailed(false);
    setData(undefined);
    void BApi.acquisition
      .searchAcquisitionCandidates({ page, pageSize: 24, filter: "all" })
      .then((response) => {
        if (!active) return;
        if (response.code || !response.data) throw new Error();
        setData(response.data);
      })
      .catch(() => {
        if (active) setFailed(true);
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [page, retry]);
  const pageIds = data?.items.map((item) => item.resourceId) ?? [];
  return (
    <Modal
      defaultVisible
      size="3xl"
      title={t("acquisition.legacyImport.title")}
      onDestroyed={onDestroyed}
      footer={{
        actions: ["cancel", "ok"],
        okProps: {
          children: t("acquisition.sharedList.import", { count: selected.length }),
          isDisabled: !selected.length || loading,
        },
      }}
      onOk={async () => {
        const response = await BApi.acquisition.importLegacyAcquisitionOverview({
          resourceIds: selected,
        });
        if (response.code || !response.data)
          throw new Error(response.message || t("acquisition.plan.actionFailed"));
        toast.success(
          t("acquisition.sharedList.imported", {
            created: response.data.created,
            matched: response.data.matched,
          }),
        );
        for (const problem of response.data.problems) toast.danger(problem);
        onImported();
      }}
    >
      <div className="space-y-3">
        <p className="text-sm text-default-500">{t("acquisition.legacyImport.description")}</p>
        {loading ? (
          <Spinner />
        ) : failed ? (
          <div role="alert" className="flex flex-wrap gap-2 text-danger">
            {t("acquisition.plan.loadFailed")}
            <Button size="sm" onPress={() => setRetry((value) => value + 1)}>
              {t("common.action.retry")}
            </Button>
          </div>
        ) : (
          <>
            <p className="text-xs text-default-500">
              {t("acquisition.legacyImport.count", {
                count: data?.totalCount ?? 0,
                selected: selected.length,
              })}
            </p>
            {!!pageIds.length && (
              <Checkbox
                isSelected={pageIds.every((id) => selected.includes(id))}
                onValueChange={(checked) =>
                  setSelected((old) =>
                    checked
                      ? [...new Set([...old, ...pageIds])]
                      : old.filter((id) => !pageIds.includes(id)),
                  )
                }
              >
                {t("acquisition.legacyImport.selectPage")}
              </Checkbox>
            )}
            <div className="space-y-2">
              {data?.items.map((item) => (
                <div key={item.resourceId} className="rounded-lg border border-default-200 p-3">
                  <Checkbox
                    isSelected={selected.includes(item.resourceId)}
                    onValueChange={(checked) =>
                      setSelected((old) =>
                        checked
                          ? [...old, item.resourceId]
                          : old.filter((id) => id !== item.resourceId),
                      )
                    }
                  >
                    <span className="break-words">
                      {item.resourceName}{" "}
                      <span className="text-xs text-default-500">#{item.resourceId}</span>
                    </span>
                  </Checkbox>
                </div>
              ))}
              {!pageIds.length && (
                <p className="text-sm text-default-500">{t("acquisition.legacyImport.empty")}</p>
              )}
            </div>
            {(data?.totalCount ?? 0) > 24 && (
              <Pagination
                page={page}
                total={Math.ceil((data?.totalCount ?? 0) / 24)}
                onChange={setPage}
              />
            )}
          </>
        )}
      </div>
    </Modal>
  );
}
