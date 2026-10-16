import type { DestroyableProps } from "@/components/bakaui/types";
import type { ReferenceProperty } from "@/components/Property/referenceValues";

import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
import { ExportOutlined } from "@ant-design/icons";

import { Button, Modal, Pagination, Spinner } from "@/components/bakaui";
import Resource from "@/components/Resource";
import { useResourceSearch } from "@/hooks/useResourceSearch";
import { usePendingSearchStore } from "@/stores/pendingSearch";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { buildReferenceValueSearch } from "@/components/Property/referenceValues";
import { resourceChangedChannel } from "@/services/ResourceChangedChannel";
import { useResourceOwner } from "@/features/management/resourceContext";
import { subscribeResourceOwnerChanges } from "@/features/management/resourceApi";

type Props = DestroyableProps & { property: ReferenceProperty; value: string; label?: string };

export default function ReferenceValueResourcesModal({
  property,
  value,
  label,
  onDestroyed,
}: Props) {
  const owner = useResourceOwner();
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { closeAllModals } = useBakabaseContext();
  const setPendingSearch = usePendingSearchStore((state) => state.setPendingSearch);
  const { resources, loading, response, search, removeResources } = useResourceSearch();
  const [page, setPage] = useState(1);
  const [error, setError] = useState(false);
  const [revision, setRevision] = useState(0);
  const searchForm = useMemo(
    () => buildReferenceValueSearch(property, value),
    [property.id, property.pool, property.type, value],
  );

  useEffect(() => {
    let cancelled = false;

    setError(false);
    search({ ...searchForm, page }, { saveSearch: false }).catch(() => {
      if (!cancelled) setError(true);
    });

    return () => {
      cancelled = true;
    };
  }, [searchForm, page, revision, search]);

  useEffect(() => {
    let timer: ReturnType<typeof setTimeout>;
    const onChanged = () => {
      clearTimeout(timer);
      timer = setTimeout(() => setRevision((value) => value + 1), 1000);
    };
    const unsubscribe =
      owner && !owner.isLocal
        ? subscribeResourceOwnerChanges(owner, onChanged)
        : resourceChangedChannel.subscribe(onChanged);

    return () => {
      unsubscribe();
      clearTimeout(timer);
    };
  }, [owner]);

  useEffect(() => {
    if (!loading && response) {
      const lastPage = Math.max(1, Math.ceil(response.totalCount / searchForm.pageSize));

      if (page > lastPage) setPage(lastPage);
    }
  }, [loading, response, page, searchForm.pageSize]);

  return (
    <Modal
      defaultVisible
      footer={false}
      size="7xl"
      title={`${property.name}: ${label ?? value}`}
      onDestroyed={onDestroyed}
    >
      <div className="flex flex-col gap-4 min-h-[320px]">
        <div className="flex items-center justify-between gap-2">
          <span className="text-sm">
            {!loading &&
              !error &&
              t("property.reference.resourceCount", { count: response?.totalCount ?? 0 })}
          </span>
          <Button
            size="sm"
            variant="flat"
            onPress={() => {
              setPendingSearch({ ...searchForm, ...(owner ? { owner } : {}) });
              closeAllModals();
              navigate("/resource");
            }}
          >
            <ExportOutlined />
            {t("property.reference.openSearchPage")}
          </Button>
        </div>
        {loading ? (
          <div className="flex justify-center py-12">
            <Spinner />
          </div>
        ) : error ? (
          <div className="text-center py-8">
            <p>{t("property.reference.loadError")}</p>
            <Button size="sm" onPress={() => setRevision((value) => value + 1)}>
              {t("property.reference.retry")}
            </Button>
          </div>
        ) : resources.length === 0 ? (
          <div className="text-center text-default-400 py-12">
            {t("property.reference.noResources")}
          </div>
        ) : (
          <div className="grid grid-cols-2 sm:grid-cols-4 lg:grid-cols-6 xl:grid-cols-8 gap-2">
            {resources.map((resource) => (
              <Resource
                key={resource.id}
                resource={resource}
                selected={false}
                selectedResourceIds={[]}
                onResourcesDeleted={(ids) => {
                  removeResources(ids);
                  setRevision((value) => value + 1);
                }}
                onSelected={() => {}}
                onSelectedResourcesChanged={() => {}}
              />
            ))}
          </div>
        )}
        {(response?.totalCount ?? 0) > searchForm.pageSize && (
          <div className="flex justify-center">
            <Pagination
              showControls
              isDisabled={loading}
              page={page}
              total={Math.ceil(response!.totalCount / searchForm.pageSize)}
              onChange={setPage}
            />
          </div>
        )}
      </div>
    </Modal>
  );
}
