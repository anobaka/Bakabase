"use client";

import type { CollectionModel } from "@/stores/collections";

import React, { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { PlusOutlined, DeleteOutlined } from "@ant-design/icons";
import { HiOutlineCollection } from "react-icons/hi";
import { useNavigate } from "react-router-dom";

import BApi from "@/sdk/BApi";
import {
  Button,
  Chip,
  Listbox,
  ListboxItem,
  Popover,
  Spinner,
  Tooltip,
  toast,
} from "@/components/bakaui";
import { ResourceAdditionalItem } from "@/sdk/constants";

interface Props {
  resourceId: number;
  onChange?: () => void;
  /** Chips only, no label — for the detail modal's block layout. */
  compact?: boolean;
}

/**
 * Which collections this belongs to, and a way in or out of one.
 *
 * A rule member is shown but cannot be removed here: it is in the collection because it matches,
 * and taking it out would mean editing the rule, which is not something to do by accident from a
 * chip's ✕.
 */
const CollectionMemberships: React.FC<Props> = ({ resourceId, onChange, compact = false }) => {
  const { t } = useTranslation();
  const navigate = useNavigate();

  const [loading, setLoading] = useState(true);
  const [all, setAll] = useState<CollectionModel[]>([]);
  const [memberOf, setMemberOf] = useState<number[]>([]);
  const [writtenDown, setWrittenDown] = useState<Set<number>>(new Set());
  const [adding, setAdding] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [collectionsRsp, mappingsRsp] = await Promise.all([
        BApi.collection.getAllCollections({ withProgress: false }),
        BApi.resource.getCollectionIdsByResourceIds({ resourceIds: [resourceId] }),
      ]);

      setAll((collectionsRsp.data ?? []) as CollectionModel[]);
      // The mapping endpoint answers with written-down memberships only, which is exactly the
      // set that can be removed from here.
      setWrittenDown(new Set(mappingsRsp.data?.[resourceId] ?? []));

      const resourceRsp = await BApi.resource.getResourcesByKeys({
        ids: [resourceId],
        additionalItems: ResourceAdditionalItem.CollectionName,
      });

      setMemberOf(((resourceRsp.data?.[0] as any)?.collections ?? []).map((c: any) => c.id));
    } finally {
      setLoading(false);
    }
  }, [resourceId]);

  useEffect(() => {
    void load();
  }, [load]);

  const add = async (collectionId: number) => {
    await BApi.collection.addCollectionMembers(collectionId, { resourceIds: [resourceId] });
    toast.success(t<string>("resource.collections.added"));
    setAdding(false);
    await load();
    onChange?.();
  };

  const remove = async (collectionId: number) => {
    await BApi.collection.removeCollectionMembers(collectionId, { resourceIds: [resourceId] });
    toast.success(t<string>("resource.collections.removed"));
    await load();
    onChange?.();
  };

  const available = all.filter((c) => !memberOf.includes(c.id));

  if (loading) {
    return (
      <div className="flex items-center gap-2">
        {!compact && <HiOutlineCollection className="text-base text-default-500" />}
        <Spinner size="sm" />
      </div>
    );
  }

  if (compact && memberOf.length === 0) return null;

  return (
    <div className="flex items-start gap-2">
      {!compact && (
        <div className="flex items-center gap-1 text-sm text-default-500 shrink-0 pt-0.5">
          <HiOutlineCollection className="text-base" />
          <span>{t<string>("resource.collections.label")}</span>
        </div>
      )}
      <div className="flex flex-wrap items-center gap-1 flex-1">
        {memberOf.length === 0 ? (
          <span className="text-sm text-default-400">{t<string>("common.label.none")}</span>
        ) : (
          memberOf.map((id) => {
            const collection = all.find((c) => c.id === id);
            const removable = writtenDown.has(id);

            return (
              <Chip
                key={id}
                endContent={
                  removable ? (
                    <Tooltip content={t<string>("resource.collections.remove")}>
                      <Button
                        isIconOnly
                        color="danger"
                        size="sm"
                        variant="light"
                        onPress={() => remove(id)}
                      >
                        <DeleteOutlined className="text-base" />
                      </Button>
                    </Tooltip>
                  ) : (
                    <Tooltip content={t<string>("resource.collections.fromRule")}>
                      <span className="text-xs px-1 opacity-60">
                        {t<string>("resource.collections.ruleShort")}
                      </span>
                    </Tooltip>
                  )
                }
                size="sm"
                style={{
                  backgroundColor: collection?.color ? `${collection.color}20` : undefined,
                  color: collection?.color ?? undefined,
                }}
                variant="flat"
                onClick={() => navigate(`/collections/detail?id=${id}`)}
              >
                <span className="cursor-pointer">
                  {collection?.name ?? t<string>("common.state.unknown")}
                </span>
              </Chip>
            );
          })
        )}
        <Popover
          placement="bottom-end"
          trigger={
            <Button
              isIconOnly
              className="min-w-6 w-6 h-6"
              color="primary"
              isDisabled={available.length === 0}
              size="sm"
              variant="light"
            >
              <PlusOutlined className="text-base" />
            </Button>
          }
          visible={adding}
          onOpenChange={setAdding}
        >
          <div className="max-h-60 overflow-y-auto min-w-[200px]">
            <Listbox
              aria-label={t<string>("resource.collections.pick")}
              onAction={(key) => add(parseInt(key as string, 10))}
            >
              {available.map((c) => (
                <ListboxItem key={c.id} textValue={c.name}>
                  <div className="flex items-center gap-2">
                    <span
                      className="w-2 h-2 rounded-full"
                      style={{ backgroundColor: c.color || "#888" }}
                    />
                    <span>{c.name}</span>
                  </div>
                </ListboxItem>
              ))}
            </Listbox>
          </div>
        </Popover>
      </div>
    </div>
  );
};

CollectionMemberships.displayName = "CollectionMemberships";

export default CollectionMemberships;
