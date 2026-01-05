"use client";

import type { Resource } from "@/core/models/Resource";
import type { DestroyableProps } from "@/components/bakaui/types";

import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import { Modal, Pagination, Spinner } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { ResourceAdditionalItem } from "@/sdk/constants";
import ResourceCover from "@/components/Resource/components/ResourceCover";

type Props = {
  resourceIds: number[];
} & DestroyableProps;

const PAGE_SIZE = 50;

const ResourcesModal = ({ resourceIds, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  const [resources, setResources] = useState<Resource[]>([]);
  const [loading, setLoading] = useState(false);

  const totalPages = Math.ceil(resourceIds.length / PAGE_SIZE);

  const loadResources = useCallback(async (pageNum: number) => {
    const startIndex = (pageNum - 1) * PAGE_SIZE;
    const endIndex = startIndex + PAGE_SIZE;
    const pageIds = resourceIds.slice(startIndex, endIndex);

    if (pageIds.length === 0) {
      setResources([]);
      return;
    }

    setLoading(true);
    try {
      const rsp = await BApi.resource.getResourcesByKeys({
        ids: pageIds,
        additionalItems: ResourceAdditionalItem.All,
      });
      setResources((rsp.data as Resource[]) || []);
    } finally {
      setLoading(false);
    }
  }, [resourceIds]);

  useEffect(() => {
    loadResources(page);
  }, [page, loadResources]);

  return (
    <Modal
      defaultVisible
      size="7xl"
      title={t("resourcesModal.title", { count: resourceIds.length })}
      onClose={onDestroyed}
      footer={null}
    >
      <div className="flex flex-col gap-2 min-h-[400px]">
        {loading ? (
          <div className="flex items-center justify-center flex-1">
            <Spinner size="lg" />
          </div>
        ) : resources.length === 0 ? (
          <div className="flex items-center justify-center flex-1 text-default-400">
            {t("common.empty.noData")}
          </div>
        ) : (
          <>
            {totalPages > 1 && (
              <div className="flex justify-center">
                <Pagination
                  page={page}
                  total={totalPages}
                  onChange={setPage}
                />
              </div>
            )}

            <div className="grid grid-cols-8 gap-2">
              {resources.map((resource) => (
                <div
                  key={resource.id}
                  className="flex flex-col gap-2 p-2 rounded-lg bg-default-50 hover:bg-default-100 transition-colors"
                >
                  <div className="aspect-[6/7] relative overflow-hidden rounded">
                    <ResourceCover
                      resource={resource}
                      showBiggerOnHover={false}
                    />
                  </div>
                  <div className="text-sm font-medium truncate" title={resource.displayName || resource.fileName}>
                    {resource.displayName || resource.fileName}
                  </div>
                </div>
              ))}
            </div>

            {totalPages > 1 && (
              <div className="flex justify-center">
                <Pagination
                  page={page}
                  total={totalPages}
                  onChange={setPage}
                />
              </div>
            )}
          </>
        )}
      </div>
    </Modal>
  );
};

ResourcesModal.displayName = "ResourcesModal";

export default ResourcesModal;
