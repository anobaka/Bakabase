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
};
const ExtensionGroupSelect = ({ value = [], onSelectionChange }: Props) => {
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
      label={t<string>("Limit file extension groups")}
      placeholder={t<string>("Select from extension groups")}
      selectedKeys={(value ?? []).map((x) => x.toString())}
      selectionMode={"multiple"}
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
