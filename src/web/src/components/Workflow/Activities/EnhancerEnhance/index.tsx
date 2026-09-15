"use client";

import type { WorkflowActivityUI } from "../types";
import type { components } from "@/sdk/BApi2";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import { Select } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { WorkflowActivityCategory } from "@/sdk/constants";

type EnhancerDescriptor =
  components["schemas"]["Bakabase.Modules.Enhancer.Abstractions.Components.IEnhancerDescriptor"];

interface Config {
  enhancerIds?: number[];
}

const ConfigForm: React.FC<{ value: Config; onChange: (v: Config) => void }> = ({
  value,
  onChange,
}) => {
  const { t } = useTranslation();
  const [enhancers, setEnhancers] = useState<EnhancerDescriptor[]>([]);

  useEffect(() => {
    BApi.enhancer
      .getAllEnhancerDescriptors()
      .then((r) => setEnhancers((r.data ?? []) as EnhancerDescriptor[]));
  }, []);

  return (
    <Select
      dataSource={enhancers.map((e) => ({
        value: String(e.id),
        label: e.name ?? String(e.id),
        textValue: e.name ?? String(e.id),
      }))}
      // Empty runs whatever the resource's profile says, which is where that decision already
      // lives — naming enhancers here is for the chain that deliberately wants only one.
      description={t<string>("workflow.enhancer.enhance.description")}
      label={t<string>("workflow.enhancer.enhance.label")}
      selectedKeys={(value.enhancerIds ?? []).map(String)}
      selectionMode="multiple"
      onSelectionChange={(keys) =>
        onChange({
          ...value,
          enhancerIds: Array.from(keys)
            .map((k) => Number(k))
            .filter((n) => !isNaN(n)),
        })
      }
    />
  );
};

export const EnhancerEnhanceUI: WorkflowActivityUI<Config> = {
  kind: "action.enhancer.enhance",
  displayNameKey: "workflow.enhancer.enhance.displayName",
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
  isValid: () => true,
  ConfigForm,
  Summary: ({ config }) => {
    const { t } = useTranslation();

    return (
      <span className="text-xs text-default-500">
        {(config.enhancerIds ?? []).length === 0
          ? t<string>("workflow.enhancer.enhance.all")
          : t<string>("workflow.enhancer.enhance.chosen", { count: config.enhancerIds!.length })}
      </span>
    );
  },
};
