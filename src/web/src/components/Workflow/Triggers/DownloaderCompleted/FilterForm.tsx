import type { DownloaderCompletedFilter } from "./types";

import React from "react";
import { useTranslation } from "react-i18next";

import ThirdPartyLabel from "@/components/ThirdPartyLabel";
import { Select } from "@/components/bakaui";
import { ThirdPartyId, thirdPartyIds } from "@/sdk/constants";

interface Props {
  value: DownloaderCompletedFilter;
  onChange: (v: DownloaderCompletedFilter) => void;
}

const FilterForm: React.FC<Props> = ({ value, onChange }) => {
  const { t } = useTranslation();

  // Reuses the codegen'd ThirdPartyId list — when a new third party is added on the
  // backend the picker picks it up without code changes here.
  return (
    <Select
      dataSource={thirdPartyIds.map(({ value: id }) => ({
        value: String(id),
        // Selected-chips render via the bakaui Select default; the textual label keeps the
        // popover lightweight. Trigger / row will surface ThirdPartyLabel via Summary.
        label: ThirdPartyId[id],
        textValue: ThirdPartyId[id],
      }))}
      description={t<string>("workflow.trigger.downloaderCompleted.thirdPartyIds.description")}
      label={t<string>("workflow.trigger.downloaderCompleted.thirdPartyIds.label")}
      selectedKeys={value.thirdPartyIds.map(String)}
      selectionMode="multiple"
      onSelectionChange={(keys) => {
        const ids = Array.from(keys).map((k) => Number(k)).filter((n) => !isNaN(n));
        onChange({ ...value, thirdPartyIds: ids });
      }}
    />
  );
};

export default FilterForm;
