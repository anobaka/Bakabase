"use client";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import BApi from "@/sdk/BApi";
import { Select } from "@/components/bakaui";

type ExtensionGroup = {
  id: number;
  name: string;
};

type Props = {
  value?: number[];
  onSelectionChange?: (ids: number[]) => void;
  size?: "sm" | "md" | "lg";
  className?: string;
};
const ExtensionGroupSelect = ({ value = [], onSelectionChange, size, className }: Props) => {
  const { t } = useTranslation();
  const [extensionGroups, setExtensionGroups] = useState<ExtensionGroup[]>([]);

  // console.log(extensionGroups, value);

  useEffect(() => {
    BApi.extensionGroup.getAllExtensionGroups().then((res) => {
      setExtensionGroups(res.data ?? []);
    });
  }, []);

  return (
    <Select
      dataSource={extensionGroups?.map((l) => ({
        label: l.name,
        value: l.id.toString(),
      }))}
      label={t<string>("extensionGroupSelect.label")}
      placeholder={t<string>("extensionGroupSelect.placeholder")}
      selectedKeys={(value ?? []).map((x) => x.toString())}
      selectionMode={"multiple"}
      size={size}
      className={className}
      onSelectionChange={(keys) => {
        onSelectionChange?.(
          Array.from(keys).map((k) => parseInt(k as string, 10)),
        );
      }}
    />
  );
};

ExtensionGroupSelect.displayName = "ExtensionGroupSelect";

export default ExtensionGroupSelect;
