import type { SubscriptionProviderFormProps } from "../types";
import type { VndbSeriesTarget } from "./types";

import React from "react";
import { useTranslation } from "react-i18next";

import { VNDB_RELATIONS } from "./types";

import { Input, Select, Switch } from "@/components/bakaui";

const VndbSeriesForm: React.FC<SubscriptionProviderFormProps<VndbSeriesTarget>> = ({
  value,
  onChange,
}) => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-3">
      <Input
        isRequired
        description={t<string>("subscription.provider.vndb.series.visualNovel.description")}
        label={t<string>("subscription.provider.vndb.series.visualNovel.label")}
        placeholder="https://vndb.org/v17"
        value={value.visualNovel}
        onValueChange={(visualNovel) => onChange({ ...value, visualNovel })}
      />
      <Select
        dataSource={VNDB_RELATIONS.map((r) => ({
          value: r.value,
          label: r.label,
          textValue: r.label,
        }))}
        description={t<string>("subscription.provider.vndb.series.relations.description")}
        label={t<string>("subscription.provider.vndb.series.relations.label")}
        selectedKeys={value.relations}
        selectionMode="multiple"
        onSelectionChange={(keys) =>
          onChange({ ...value, relations: Array.from(keys).map(String) })
        }
      />
      <Switch
        isSelected={value.officialOnly}
        size="sm"
        onValueChange={(officialOnly) => onChange({ ...value, officialOnly })}
      >
        <span className="text-sm">
          {t<string>("subscription.provider.vndb.series.officialOnly")}
        </span>
      </Switch>
      <Switch
        isSelected={value.includeSelf}
        size="sm"
        onValueChange={(includeSelf) => onChange({ ...value, includeSelf })}
      >
        <span className="text-sm">
          {t<string>("subscription.provider.vndb.series.includeSelf")}
        </span>
      </Switch>
    </div>
  );
};

export default VndbSeriesForm;
