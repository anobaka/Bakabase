"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { SearchFilter } from "../../models";

import { useRef } from "react";
import { useTranslation } from "react-i18next";

import Filter from "../Filter";

import { Modal } from "@/components/bakaui";

type Props = {
  filter: SearchFilter;
  onSubmit: (filter: SearchFilter) => void;
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
