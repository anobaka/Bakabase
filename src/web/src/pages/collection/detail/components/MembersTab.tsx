"use client";

import type { CollectionModel } from "@/stores/collections";
import type { Resource as ResourceModel } from "@/core/models/Resource";
import type { IProperty } from "@/components/Property/models";

import React, { useCallback, useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlinePlusCircle } from "react-icons/ai";
import { SelectItem } from "@heroui/react";

import BApi from "@/sdk/BApi";
import Resource from "@/components/Resource";
import {
  Button,
  Chip,
  Input,
  Modal,
  Pagination,
  Select,
  Spinner,
  Tooltip,
  toast,
} from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { CollectionMemberFilter, PropertyPool, ResourceAdditionalItem } from "@/sdk/constants";
import { isNonEmptyValue } from "@/core/models/Resource";

const PAGE_SIZE = 40;

const FILTERS: CollectionMemberFilter[] = [
  CollectionMemberFilter.All,
  CollectionMemberFilter.Owned,
  CollectionMemberFilter.Missing,
  CollectionMemberFilter.Acquiring,
  CollectionMemberFilter.Ignored,
];

const FILTER_KEY: Record<number, string> = {
  [CollectionMemberFilter.All]: "collection.member.filter.all",
  [CollectionMemberFilter.Owned]: "collection.member.filter.owned",
  [CollectionMemberFilter.Missing]: "collection.member.filter.missing",
  [CollectionMemberFilter.Acquiring]: "collection.member.filter.acquiring",
  [CollectionMemberFilter.Ignored]: "collection.member.filter.ignored",
};

type Props = {
  collection: CollectionModel;
  onChanged: () => void;
};

type Membership = {
  resourceId: number;
  isIgnored: boolean;
  isFromRule: boolean;
};

/**
 * The members, and what state each is in.
 *
 * A collection is mostly read here, so the grid is the resource grid — the same card, the same
 * right-click menu. What is added is the two things only a collection knows: whether a member is
 * here because somebody put it here or because a rule caught it, and whether it has been set aside.
 */
const MembersTab: React.FC<Props> = ({ collection, onChanged }) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [filter, setFilter] = useState<CollectionMemberFilter>(CollectionMemberFilter.All);
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState(0);
  const [resources, setResources] = useState<ResourceModel[]>([]);
  const [memberships, setMemberships] = useState<Map<number, Membership>>(new Map());
  const [loading, setLoading] = useState(true);
  const [groupPropertyId, setGroupPropertyId] = useState<string>("");
  const [properties, setProperties] = useState<IProperty[]>([]);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [pageRsp, membershipRsp] = await Promise.all([
        BApi.collection.searchCollectionMembers(collection.id, {
          filter,
          pageIndex: page,
          pageSize: PAGE_SIZE,
        }),
        BApi.collection.getCollectionMemberships(collection.id),
      ]);

      const ids = pageRsp.data?.resourceIds ?? [];

      setTotal(pageRsp.data?.totalCount ?? 0);
      setMemberships(
        new Map(
          (membershipRsp.data ?? []).map((m) => [
            m.resourceId,
            { resourceId: m.resourceId, isIgnored: m.isIgnored, isFromRule: m.isFromRule },
          ]),
        ),
      );

      if (ids.length === 0) {
        setResources([]);
      } else {
        const resourcesRsp = await BApi.resource.getResourcesByKeys({
          ids,
          additionalItems: ResourceAdditionalItem.All,
        });
        const byId = new Map((resourcesRsp.data ?? []).map((r: any) => [r.id, r]));

        // Kept in the order the collection gave them: a series that was reordered by hand
        // reads in that order, not in whatever order the resource endpoint answered.
        setResources(ids.map((id) => byId.get(id)).filter(Boolean) as ResourceModel[]);
      }
    } finally {
      setLoading(false);
    }
  }, [collection.id, filter, page]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    BApi.property
      .getPropertiesByPool((PropertyPool.Custom | PropertyPool.Reserved) as any)
      .then((r) => setProperties((r.data ?? []) as IProperty[]));
  }, []);

  const setIgnored = async (resourceId: number, ignored: boolean) => {
    await BApi.collection.setCollectionMemberIgnored(collection.id, resourceId, { ignored });
    await load();
    onChanged();
  };

  const removeMember = async (resourceId: number) => {
    await BApi.collection.removeCollectionMembers(collection.id, { resourceIds: [resourceId] });
    await load();
    onChanged();
  };

  const addPlaceholder = () => {
    let title = "";

    createPortal(Modal, {
      defaultVisible: true,
      title: t<string>("collection.member.addPlaceholder"),
      children: (
        <div className="flex flex-col gap-2">
          <div className="text-sm text-default-500">
            {t<string>("collection.member.addPlaceholderDescription")}
          </div>
          <Input
            label={t<string>("collection.member.title")}
            onValueChange={(v) => {
              title = v;
            }}
          />
        </div>
      ),
      onOk: async () => {
        if (!title.trim()) {
          toast.danger(t<string>("collection.member.titleRequired"));

          return;
        }
        await BApi.collection.addCollectionPlaceholderMember(collection.id, {
          title: title.trim(),
        });
        await load();
        onChanged();
      },
    });
  };

  /**
   * Members grouped by one property's value. A series with fifty volumes across four authors is
   * unreadable as one flat grid, and the grouping people want is never the same twice.
   */
  const groups = useMemo(() => {
    if (!groupPropertyId) return [{ label: "", resources }];

    const [poolStr, idStr] = groupPropertyId.split(":");
    const pool = Number(poolStr) as PropertyPool;
    const id = Number(idStr);
    const byLabel = new Map<string, ResourceModel[]>();

    for (const resource of resources) {
      // `values` carries one entry per scope, empty ones included, and the highest-priority
      // scope is often the empty Manual row — take the first that actually holds something.
      const value = resource.properties?.[pool]?.[id]?.values?.find((v) =>
        isNonEmptyValue(v?.aliasAppliedBizValue ?? v?.bizValue),
      );
      const bizValue = value?.aliasAppliedBizValue ?? value?.bizValue;
      const label =
        bizValue == null
          ? t<string>("collection.member.groupNone")
          : Array.isArray(bizValue)
            ? bizValue.join(", ")
            : String(bizValue);

      byLabel.set(label, [...(byLabel.get(label) ?? []), resource]);
    }

    return [...byLabel.entries()]
      .sort((a, b) => a[0].localeCompare(b[0]))
      .map(([label, rs]) => ({ label, resources: rs }));
  }, [groupPropertyId, resources, t]);

  const renderResource = (resource: ResourceModel) => {
    const membership = memberships.get(resource.id);

    return (
      <div key={resource.id} className="flex flex-col gap-1">
        <Resource
          resource={resource}
          selected={false}
          selectedResourceIds={[]}
          onSelected={() => {}}
          onSelectedResourcesChanged={() => {}}
        />
        <div className="flex items-center gap-1">
          {membership?.isFromRule && (
            <Tooltip content={t<string>("collection.member.fromRuleTip")}>
              <Chip color="secondary" size="sm" variant="flat">
                {t<string>("collection.member.fromRule")}
              </Chip>
            </Tooltip>
          )}
          <Button
            size="sm"
            variant="light"
            onPress={() => setIgnored(resource.id, !membership?.isIgnored)}
          >
            {membership?.isIgnored
              ? t<string>("collection.member.unignore")
              : t<string>("collection.member.ignore")}
          </Button>
          {membership && !membership.isFromRule && (
            <Button
              color="danger"
              size="sm"
              variant="light"
              onPress={() => removeMember(resource.id)}
            >
              {t<string>("collection.member.remove")}
            </Button>
          )}
        </div>
      </div>
    );
  };

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2">
        {FILTERS.map((f) => (
          <Button
            key={f}
            color={filter === f ? "primary" : "default"}
            size="sm"
            variant={filter === f ? "solid" : "light"}
            onPress={() => {
              setFilter(f);
              setPage(1);
            }}
          >
            {t<string>(FILTER_KEY[f])}
          </Button>
        ))}

        <Select
          aria-label={t<string>("collection.member.groupBy")}
          className="max-w-56 ml-2"
          placeholder={t<string>("collection.member.groupBy")}
          selectedKeys={groupPropertyId ? [groupPropertyId] : []}
          size="sm"
          onSelectionChange={(keys) => setGroupPropertyId(([...keys][0] as string) ?? "")}
        >
          {[
            <SelectItem key="" textValue={t<string>("collection.member.groupNothing")}>
              {t<string>("collection.member.groupNothing")}
            </SelectItem>,
            ...properties.map((p) => (
              <SelectItem key={`${p.pool}:${p.id}`} textValue={p.name}>
                {p.name}
              </SelectItem>
            )),
          ]}
        </Select>

        <Button
          className="ml-auto"
          size="sm"
          startContent={<AiOutlinePlusCircle className="text-base" />}
          variant="light"
          onPress={addPlaceholder}
        >
          {t<string>("collection.member.addPlaceholder")}
        </Button>
      </div>

      {loading ? (
        <div className="flex justify-center py-10">
          <Spinner />
        </div>
      ) : resources.length === 0 ? (
        <div className="text-center text-default-400 py-10">
          {t<string>("collection.member.empty")}
        </div>
      ) : (
        <div className="flex flex-col gap-4">
          {groups.map((group) => (
            <div key={group.label || "__all"} className="flex flex-col gap-2">
              {group.label && (
                <div className="text-sm text-default-500 font-medium">
                  {group.label} · {group.resources.length}
                </div>
              )}
              <div className="grid grid-cols-2 sm:grid-cols-4 lg:grid-cols-6 xl:grid-cols-8 gap-2">
                {group.resources.map(renderResource)}
              </div>
            </div>
          ))}
        </div>
      )}

      {total > PAGE_SIZE && (
        <div className="flex justify-center">
          <Pagination
            showControls
            page={page}
            total={Math.ceil(total / PAGE_SIZE)}
            onChange={setPage}
          />
        </div>
      )}
    </div>
  );
};

MembersTab.displayName = "MembersTab";

export default MembersTab;
