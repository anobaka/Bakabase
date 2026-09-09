"use client";

import type { WorkflowActivityUI } from "../types";
import type { CollectionModel } from "@/stores/collections";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import { Select } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { WorkflowActivityCategory } from "@/sdk/constants";

interface Config {
  collectionId?: number;
}

const ConfigForm: React.FC<{ value: Config; onChange: (v: Config) => void }> = ({
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

  return (
    <Select
      dataSource={collections.map((c) => ({
        value: String(c.id),
        label: c.name,
        textValue: c.name,
      }))}
      description={t<string>("workflow.collection.addResource.collection.description")}
      label={t<string>("workflow.collection.addResource.collection.label")}
      selectedKeys={value.collectionId ? [String(value.collectionId)] : []}
      onSelectionChange={(keys) => {
        const id = Number(Array.from(keys)[0]);

        onChange({ collectionId: isNaN(id) ? undefined : id });
      }}
    />
  );
};

const Summary: React.FC<{ config: Config }> = ({ config }) => {
  const { t } = useTranslation();
  const [name, setName] = useState<string>();

  useEffect(() => {
    if (!config.collectionId) return;
    BApi.collection.getCollection(config.collectionId).then((r) => setName(r.data?.name));
  }, [config.collectionId]);

  if (!config.collectionId) {
    return (
      <span className="text-xs text-danger">
        {t<string>("workflow.collection.addResource.notChosen")}
      </span>
    );
  }

  return <span className="text-xs text-default-500">{name ?? `#${config.collectionId}`}</span>;
};

export const CollectionAddResourceUI: WorkflowActivityUI<Config> = {
  kind: "action.collection.addResource",
  displayNameKey: "workflow.collection.addResource.displayName",
  category: WorkflowActivityCategory.Action,
  defaultConfig: () => ({}),
  parseConfig: (json) => {
    try {
      return json ? (JSON.parse(json) as Config) : {};
    } catch {
      return {};
    }
  },
  serializeConfig: (config) => JSON.stringify(config),
  // Without a collection the run would fail at this step; saying so in the editor is cheaper
  // than saying it in a run history.
  isValid: (config) => !!config.collectionId,
  ConfigForm,
  Summary,
};
