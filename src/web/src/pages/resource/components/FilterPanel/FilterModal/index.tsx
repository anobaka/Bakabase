import type { DestroyableProps } from "@/components/bakaui/types";
import type { ResourceSearchFilter } from "../FilterGroupsPanel/models";

import { useEffect, useRef } from "react";
import { useTranslation } from "react-i18next";

import Filter from "../FilterGroupsPanel/FilterGroup/Filter";

import { Modal } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import PropertySelector from "@/components/PropertySelector";
import { PropertyPool, SearchOperation } from "@/sdk/constants";
import { useUpdate } from "react-use";

type Props = {
  filter: ResourceSearchFilter;
  onSubmit: (filter: ResourceSearchFilter) => void;
  isNew?: boolean;
} & DestroyableProps;

const FilterModal = ({ filter, onSubmit, isNew }: Props) => {
  const { t } = useTranslation();
  const filterRef = useRef(filter);

  return (
    <Modal
      defaultVisible
      size="sm"
      title={t<string>("Configure filter")}
      onOk={async () => {
        onSubmit(filterRef.current);
      }}
    >
      <div className="flex items-center justify-center">
        <Filter
          isNew={isNew}
          disableTooltipOperations
          filter={filter}
          onChange={(f) => {
            filterRef.current = f;
          }}
        />
      </div>
    </Modal>
  );
};

export default FilterModal;