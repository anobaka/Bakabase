import type { Props } from "@/components/Property/components/PropertyValueRenderer";
import type { IChoice } from "@/components/Property/models";
import type { FilterOwner } from "../../models";

import { useTranslation } from "react-i18next";
import { useMemo } from "react";

import FilterValueRenderer from "./FilterValueRenderer";

import { mediaLibraryChoiceValue } from "@/features/management/mediaLibraryFilter";
import { orderMediaLibraryChoices } from "@/features/management/mediaLibraryOrder";
import {
  createMediaLibraryOrderSource,
  useMediaLibraryOrder,
} from "@/features/management/useMediaLibraryOrder";
import MediaLibraryOrderToolbar from "@/features/management/MediaLibraryOrderToolbar";

type MediaLibraryProps = Props & { orderOwner?: FilterOwner; qualified?: boolean };

/** Catalog presentation only: native/qualified selection IDs and resource search order stay intact. */
export default function MediaLibraryFilterValue({
  orderOwner,
  qualified,
  ...props
}: MediaLibraryProps) {
  const { t } = useTranslation();
  const choices = (props.property.options?.choices ?? []) as IChoice[];
  const owner = orderOwner ?? props.property.owner;
  const key = (choice: IChoice) =>
    qualified || !owner ? choice.value : mediaLibraryChoiceValue(owner, choice.value);
  const order = useMediaLibraryOrder(choices.map(key));
  const ordered = orderMediaLibraryChoices(choices, order.keys, key);
  const enabled = Boolean(qualified || owner) && !props.isReadonly;
  const catalogKey = JSON.stringify(choices.map((choice) => [choice.value, key(choice)]));
  const source = useMemo(
    () =>
      createMediaLibraryOrderSource(
        choices.map((choice) => ({ value: choice.value, key: key(choice) })),
      ),
    [catalogKey],
  );

  return (
    <FilterValueRenderer
      {...props}
      key={JSON.stringify(choices.map(({ value, label, color }) => [value, label, color]))}
      optionOrdering={
        enabled
          ? {
              toolbar: (
                <MediaLibraryOrderToolbar
                  entries={choices.map((choice) => ({
                    key: key(choice),
                    label: choice.label ?? "",
                  }))}
                />
              ),
              source,
              failureLabel: t("mediaLibrary.optionOrder.saveFailed"),
              dragLabel: (name) => t("mediaLibrary.optionOrder.drag", { name }),
              onMove: async (active, over, visible) => {
                const toKey = (value: string) => key({ value });

                return order.move(toKey(active), toKey(over), visible.map(toKey));
              },
            }
          : undefined
      }
      property={{ ...props.property, options: { ...props.property.options, choices: ordered } }}
    />
  );
}
