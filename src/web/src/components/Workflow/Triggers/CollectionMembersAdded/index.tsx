"use client";

import type { WorkflowTriggerUI } from "../types";
import type { CollectionModel } from "@/stores/collections";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import { Select } from "@/components/bakaui";
import { WorkflowItemTypes } from "@/components/Workflow/itemTypes";
import BApi from "@/sdk/BApi";
import { CollectionMembershipOrigin, CollectionMembershipOriginLabel } from "@/sdk/constants";

interface Filter {
  collectionIds: number[];
  origins: number[];
}

const EMPTY: Filter = { collectionIds: [], origins: [] };

const FilterForm: React.FC<{ value: Filter; onChange: (v: Filter) => void }> = ({
  value,
  onChange,
}) => {
  const { t } = useTranslation();
  const [collections, setCollections] = useState<CollectionModel[]>([]);

  useEffect(() => {
    BApi.collection
      .getAllCollections({ withProgress: false })
      .then((r) => setCollections((r.data ?? []) as CollectionModel[]));
  }, []);

  const ids = (keys: Iterable<unknown>) =>
    Array.from(keys)
      .map((k) => Number(k))
      .filter((n) => !isNaN(n));

  return (
    <div className="flex flex-col gap-2">
      <Select
        dataSource={collections.map((c) => ({
          value: String(c.id),
          label: c.name,
          textValue: c.name,
        }))}
        description={t<string>("workflow.trigger.collectionMembersAdded.collections.description")}
        label={t<string>("workflow.trigger.collectionMembersAdded.collections.label")}
        selectedKeys={value.collectionIds.map(String)}
        selectionMode="multiple"
        onSelectionChange={(keys) => onChange({ ...value, collectionIds: ids(keys) })}
      />
      <Select
        dataSource={[
          CollectionMembershipOrigin.Manual,
          CollectionMembershipOrigin.Subscription,
        ].map((o) => ({
          value: String(o),
          label: CollectionMembershipOriginLabel[o],
          textValue: CollectionMembershipOriginLabel[o],
        }))}
        description={t<string>("workflow.trigger.collectionMembersAdded.origins.description")}
        label={t<string>("workflow.trigger.collectionMembersAdded.origins.label")}
        selectedKeys={value.origins.map(String)}
        selectionMode="multiple"
        onSelectionChange={(keys) => onChange({ ...value, origins: ids(keys) })}
      />
    </div>
  );
};

const FilterSummary: React.FC<{ filter: Filter }> = ({ filter }) => {
  const { t } = useTranslation();
  const parts: string[] = [];

  if (filter.collectionIds.length > 0) {
    parts.push(
      t<string>("workflow.trigger.collectionMembersAdded.summary.collections", {
        count: filter.collectionIds.length,
      }),
    );
  }
  if (filter.origins.length > 0) {
    parts.push(filter.origins.map((o) => CollectionMembershipOriginLabel[o as never]).join(", "));
  }

  return (
    <span className="text-xs text-default-500">
      {parts.length === 0
        ? t<string>("workflow.trigger.collectionMembersAdded.summary.matchAll")
        : parts.join(" · ")}
    </span>
  );
};

export const CollectionMembersAddedTriggerUI: WorkflowTriggerUI<Filter> = {
  kind: "collection.membersAdded",
  displayNameKey: "workflow.trigger.collectionMembersAdded.displayName",
  defaultFilter: () => ({ ...EMPTY }),
  parseFilter: (json) => {
    if (!json) return { ...EMPTY };
    try {
      const parsed = JSON.parse(json) as Partial<Filter>;

      return { collectionIds: parsed.collectionIds ?? [], origins: parsed.origins ?? [] };
    } catch {
      return { ...EMPTY };
    }
  },
  serializeFilter: (filter) =>
    filter.collectionIds.length === 0 && filter.origins.length === 0
      ? null
      : JSON.stringify(filter),
  isValid: () => true,
  resolveOutputItemType: () => WorkflowItemTypes.CollectionMember,
  FilterForm,
  FilterSummary,
};
