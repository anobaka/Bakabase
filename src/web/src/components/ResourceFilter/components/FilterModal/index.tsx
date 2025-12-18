"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { SearchFilter } from "../../models";

import { useMemo, useRef } from "react";
import { useTranslation } from "react-i18next";

import Filter from "../Filter";
import { FilterProvider } from "../../context/FilterContext";
import { createDefaultFilterConfig } from "../../presets/DefaultFilterPreset";

import { Modal } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

type Props = {
  filter: SearchFilter;
  onSubmit: (filter: SearchFilter) => void;
  isNew?: boolean;
} & DestroyableProps;

const FilterModal = ({ filter, onSubmit, isNew }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const filterRef = useRef(filter);
  const filterConfig = useMemo(() => createDefaultFilterConfig(createPortal), [createPortal]);

  return (
    <Modal
      defaultVisible
      size="sm"
      title={t<string>("Configure filter")}
      onOk={async () => {
        onSubmit(filterRef.current);
      }}
    >
      <FilterProvider config={filterConfig}>
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
      </FilterProvider>
    </Modal>
  );
};

export default FilterModal;
