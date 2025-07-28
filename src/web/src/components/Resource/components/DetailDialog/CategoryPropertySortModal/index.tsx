"use client";

import type { PropertyType } from "@/sdk/constants";
import type { DestroyableProps } from "@/components/bakaui/types";

import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";

import { Modal } from "@/components/bakaui";
import BlockSort from "@/components/BlockSort";
import BApi from "@/sdk/BApi";

type PropertyLike = {
  id: number;
  name: string;
  type: PropertyType;
};

type Props = {
  categoryId: number;
  properties: PropertyLike[];
} & DestroyableProps;
const CategoryPropertySortModal = ({
  properties,
  onDestroyed,
  categoryId,
  onClose,
}: Props) => {
  const { t } = useTranslation();

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["cancel"],
        cancelProps: {
          text: t<string>("Close"),
        },
      }}
      size={"xl"}
      title={t<string>("Adjust orders of properties")}
      onClose={onClose}
      onDestroyed={onDestroyed}
    >
      <div>
        {t<string>(
          "You can adjust orders or properties by dragging and dropping them",
        )}
      </div>
      <BlockSort
        blocks={properties}
        onSorted={async (ids) => {
          await BApi.category.sortCustomPropertiesInCategory(categoryId, {
            orderedPropertyIds: ids,
          });
          toast.success(t<string>("Saved"));
        }}
      />
    </Modal>
  );
};

CategoryPropertySortModal.displayName = "CategoryPropertySortModal";

export default CategoryPropertySortModal;
