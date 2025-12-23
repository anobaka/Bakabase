"use client";

import type { Resource as ResourceModel } from "@/core/models/Resource";

import React, { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  Drawer,
  DrawerBody,
  DrawerContent,
  DrawerHeader,
} from "@heroui/react";

import BApi from "@/sdk/BApi";
import { Pagination, Spinner } from "@/components/bakaui";
import Resource from "@/components/Resource";
import {
  PropertyPool,
  ResourceProperty,
  ResourceSearchSortableProperty,
  SearchCombinator,
  SearchOperation,
} from "@/sdk/constants";

interface IProps {
  isOpen: boolean;
  onClose: () => void;
}

const PAGE_SIZE = 100;

const RecentlyPlayedDrawer = ({ isOpen, onClose }: IProps) => {
  const { t } = useTranslation();

  const [resources, setResources] = useState<ResourceModel[]>([]);
  const [loading, setLoading] = useState(false);
  const [page, setPage] = useState(1);
  const [totalCount, setTotalCount] = useState(0);

  const loadResources = useCallback(async (pageIndex: number) => {
    setLoading(true);
    try {
      const response = await BApi.resource.searchResources({
        pageSize: PAGE_SIZE,
        page: pageIndex,
        orders: [
          {
            property: ResourceSearchSortableProperty.PlayedAt,
            asc: false,
          },
        ],
        group: {
          combinator: SearchCombinator.And,
          filters: [
            {
              propertyPool: PropertyPool.Internal,
              propertyId: ResourceProperty.PlayedAt,
              operation: SearchOperation.IsNotNull,
              disabled: false,
            },
          ],
          disabled: false,
        },
      });

      if (!response.code) {
        setResources((response.data ?? []) as ResourceModel[]);
        setTotalCount(response.totalCount ?? 0);
      }
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (isOpen) {
      loadResources(page);
    }
  }, [isOpen, page, loadResources]);

  const totalPages = Math.ceil(totalCount / PAGE_SIZE);

  return (
    <Drawer
      isOpen={isOpen}
      placement="right"
      size="xl"
      onClose={onClose}
    >
      <DrawerContent>
        <DrawerHeader className="flex flex-col gap-1">
          {t("Recently played")}
          {totalCount > 0 && (
            <span className="text-sm text-default-500">
              {t("{{count}} resources", { count: totalCount })}
            </span>
          )}
        </DrawerHeader>
        <DrawerBody>
          {loading ? (
            <div className="flex justify-center items-center h-full">
              <Spinner size="lg" />
            </div>
          ) : resources.length === 0 ? (
            <div className="flex justify-center items-center h-full text-default-500">
              {t("No recently played resources")}
            </div>
          ) : (
            <div className="flex flex-col gap-4">
              {totalPages > 1 && (
                <div className="flex justify-center">
                  <Pagination
                    page={page}
                    size="sm"
                    total={totalPages}
                    onChange={(p) => setPage(p)}
                  />
                </div>
              )}
              <div className="grid grid-cols-3 gap-2">
                {resources.map((resource) => (
                  <Resource
                    key={resource.id}
                    resource={resource}
                  />
                ))}
              </div>
              {totalPages > 1 && (
                <div className="flex justify-center">
                  <Pagination
                    page={page}
                    size="sm"
                    total={totalPages}
                    onChange={(p) => setPage(p)}
                  />
                </div>
              )}
            </div>
          )}
        </DrawerBody>
      </DrawerContent>
    </Drawer>
  );
};

RecentlyPlayedDrawer.displayName = "RecentlyPlayedDrawer";

export default RecentlyPlayedDrawer;
