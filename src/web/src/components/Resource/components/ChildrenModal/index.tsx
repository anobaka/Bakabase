"use client";

import type { DestroyableProps } from "@/components/bakaui/types";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import { Modal, Spinner } from "@/components/bakaui";
import Resource from "@/components/Resource";
import { buildLogger } from "@/components/utils";
import {
  SearchCombinator,
  SearchOperation,
  PropertyPool,
  InternalProperty,
} from "@/sdk/constants";
import { useResourceSearch } from "@/hooks/useResourceSearch";

const log = buildLogger("ChildrenModal");

type ChildrenModalProps = {
  resourceId: number;
} & DestroyableProps;

const ChildrenModal: React.FC<ChildrenModalProps> = ({
  resourceId,
  onDestroyed,
}) => {
  const { t } = useTranslation();
  const { resources: children, loading, search } = useResourceSearch();
  const [error, setError] = useState<string | null>(null);

  // 当Modal显示且resourceId变化时，重新搜索
  useEffect(() => {
    if (resourceId) {
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
          page: 1,
          pageSize: 1000,
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
    }
  }, [resourceId]);

  return (
    <Modal
      defaultVisible
      footer={false}
      size="xl"
      title={t<string>("Resource Children")}
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
          <div className="flex flex-col gap-2">
            <div>
              {t<string>("Found {{count}} children", {
                count: children.length,
              })}
            </div>
            <div className="grid grid-cols-6 gap-4">
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
          </div>
        )}
      </div>
    </Modal>
  );
};

export default ChildrenModal;
