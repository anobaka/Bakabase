import type { ResourceMaterializedFilter } from "./types";

import React from "react";
import { useTranslation } from "react-i18next";

import { Select } from "@/components/bakaui";
import { ResourceSourceLabel, resourceSources } from "@/sdk/constants";

interface Props {
  value: ResourceMaterializedFilter;
  onChange: (v: ResourceMaterializedFilter) => void;
}

const FilterForm: React.FC<Props> = ({ value, onChange }) => {
  const { t } = useTranslation();

  // Driven by the generated source list, so a platform added on the backend shows up here
  // without a frontend change.
  return (
    <Select
      dataSource={resourceSources.map(({ value: id }) => ({
        value: String(id),
        label: ResourceSourceLabel[id],
        textValue: ResourceSourceLabel[id],
      }))}
      description={t<string>("workflow.trigger.resourceMaterialized.sources.description")}
      label={t<string>("workflow.trigger.resourceMaterialized.sources.label")}
      selectedKeys={value.sources.map(String)}
      selectionMode="multiple"
      onSelectionChange={(keys) => {
        const ids = Array.from(keys)
          .map((k) => Number(k))
          .filter((n) => !isNaN(n));

        onChange({ ...value, sources: ids });
      }}
    />
  );
};

export default FilterForm;
