"use client";

import type { DestroyableProps } from "@/components/bakaui/types";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate, useLocation } from "react-router-dom";

import { Modal, Spinner, Pagination, Button, Tooltip } from "@/components/bakaui";
import Resource from "@/components/Resource";
import { buildLogger } from "@/components/utils";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { usePendingSearchStore } from "@/stores/pendingSearch";
import {
  SearchCombinator,
  SearchOperation,
  PropertyPool,
  InternalProperty,
} from "@/sdk/constants";
import { useResourceSearch } from "@/hooks/useResourceSearch";
import { AiOutlineSearch } from "react-icons/ai";

const log = buildLogger("ChildrenModal");

type ChildrenModalProps = {
  resourceId: number;
  /** Parent resource displayName - used for filter bizValue */
  resourceDisplayName?: string;
} & DestroyableProps;

const PAGE_SIZE = 50;

const ChildrenModal: React.FC<ChildrenModalProps> = ({
  resourceId,
  resourceDisplayName,
  onDestroyed,
}) => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();
  const { closeAllModals } = useBakabaseContext();
  const setPendingSearch = usePendingSearchStore((s) => s.setPendingSearch);
  const { resources: children, loading, response, search } = useResourceSearch();
  const [error, setError] = useState<string | null>(null);
  const [page, setPage] = useState(1);

  const handleShowInNewTab = () => {
    const searchModel = {
      group: {
        combinator: SearchCombinator.And,
        disabled: false,
        filters: [
          {
            propertyPool: PropertyPool.Internal,
            propertyId: InternalProperty.ParentResource,
            operation: SearchOperation.Equals,
            dbValue: resourceId.toString(),
            bizValue: resourceDisplayName,
            disabled: false,
          },
        ],
      },
      page: 1,
      pageSize: 50,
    };

    // Set the pending search in store
    setPendingSearch(searchModel);
    // Close all modals
    closeAllModals();
    // Navigate to resource page if not already there
    if (location.pathname !== "/resource") {
      navigate("/resource");
    }
  };

  const doSearch = (pageNumber: number) => {
    setError(null);
    search(
      {
        group: {
          combinator: SearchCombinator.And,
          disabled: false,
          filters: [
            {
              propertyPool: PropertyPool.Internal,
              propertyId: InternalProperty.ParentResource,
              operation: SearchOperation.Equals,
              dbValue: resourceId.toString(),
              disabled: false,
            },
          ],
        },
        page: pageNumber,
        pageSize: PAGE_SIZE,
      },
      { saveSearch: false },
    )
      .then((result) => {
        log("Found children:", result.length);
      })
      .catch((err) => {
        console.error("Failed to search children:", err);
        setError(t<string>("Failed to load children"));
      });
  };

  // 当Modal显示且resourceId变化时，重新搜索
  useEffect(() => {
    if (resourceId) {
      setPage(1);
      doSearch(1);
    }
  }, [resourceId]);

  const handlePageChange = (newPage: number) => {
    setPage(newPage);
    doSearch(newPage);
  };

  const totalPages = response ? Math.ceil(response.totalCount / PAGE_SIZE) : 0;

  return (
    <Modal
      defaultVisible
      footer={false}
      size="7xl"
      title={(
        <div className="flex items-center gap-2">
          <span>{t<string>("Resource Children")}</span>
          <Tooltip content={t("View children in new resource tab")}>
            <Button
              isIconOnly
              size="sm"
              variant="light"
              onPress={handleShowInNewTab}
            >
              <AiOutlineSearch className="text-base" />
            </Button>
          </Tooltip>
        </div>
      )}
      onDestroyed={onDestroyed}
    >
      <div className="p-4">
        {loading ? (
          <div className="flex justify-center items-center h-32">
            <Spinner size="lg" />
            <span className="ml-2">{t<string>("Loading children...")}</span>
          </div>
        ) : error ? (
          <div className="text-center text-red-500 p-4">{error}</div>
        ) : children.length === 0 ? (
          <div className="text-center text-gray-500 p-4">
            {t<string>("No children found")}
          </div>
        ) : (
          <div className="flex flex-col gap-4">
            <div>
              {t<string>("Found {{count}} children", {
                count: response?.totalCount ?? children.length,
              })}
            </div>
            <div className="grid grid-cols-8 gap-1">
              {children.map((child) => (
                <Resource
                  key={child.id}
                  resource={child}
                  selected={false}
                  selectedResourceIds={[]}
                  onSelected={() => {}}
                  onSelectedResourcesChanged={() => {}}
                />
              ))}
            </div>
            {totalPages > 1 && (
              <div className="flex justify-center mt-4">
                <Pagination
                  showControls
                  page={page}
                  total={totalPages}
                  onChange={handlePageChange}
                />
              </div>
            )}
          </div>
        )}
      </div>
    </Modal>
  );
};

export default ChildrenModal;
