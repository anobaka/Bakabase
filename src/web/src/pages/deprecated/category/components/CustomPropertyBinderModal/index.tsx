"use client";

import type { DestroyableProps } from "@/components/bakaui/types";

import { useTranslation } from "react-i18next";

import PropertySelector from "@/components/PropertySelector";
import BApi from "@/sdk/BApi";
import { PropertyPool } from "@/sdk/constants";

type Props = {
  category: { id: number; name: string; customProperties: { id: number }[] };
  onSaved?: () => any;
} & DestroyableProps;
const CustomPropertyBinderModal = ({ category, onSaved, onDestroyed }: Props) => {
  const { t } = useTranslation();

  return (
    <PropertySelector
      addable
      multiple
      pool={PropertyPool.Custom}
      selection={category.customProperties?.map((c) => ({
        id: c.id,
        pool: PropertyPool.Custom,
      }))}
      title={t<string>("Binding custom properties to category {{categoryName}}", {
        categoryName: category.name,
      })}
      onDestroyed={onDestroyed}
      onSubmit={async (properties) => {
        const rsp = await BApi.category.bindCustomPropertiesToCategory(category.id, {
          customPropertyIds: properties?.map((p) => p.id),
        });

        if (!rsp.code) {
          onSaved?.();
        } else {
          throw rsp;
        }
      }}
    />
  );
};

CustomPropertyBinderModal.displayName = "CustomPropertyBinderModal";

export default CustomPropertyBinderModal;
