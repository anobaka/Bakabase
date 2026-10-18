import type { Props } from "@/components/Property/components/PropertyValueRenderer";
import type { IChoice } from "@/components/Property/models";
import type { FilterOwner } from "../../models";

import FilterValueRenderer from "./FilterValueRenderer";

import { useConfiguredMediaLibraryChoices } from "@/features/management/mediaLibraryChoiceOrder";

type MediaLibraryProps = Props & { orderOwner?: FilterOwner; qualified?: boolean };

/** Catalog presentation only: native/qualified selection IDs and resource search order stay intact. */
export default function MediaLibraryFilterValue({
  orderOwner,
  qualified,
  ...props
}: MediaLibraryProps) {
  const configured = useConfiguredMediaLibraryChoices(
    (props.property.options?.choices ?? []) as IChoice[],
    (choice) => choice.value,
    { owner: orderOwner ?? props.property.owner, qualified },
  );

  return (
    <FilterValueRenderer
      {...props}
      key={JSON.stringify(
        (props.property.options?.choices ?? []).map(({ value, label, color }: IChoice) => [
          value,
          label,
          color,
        ]),
      )}
      optionOrderSource={configured.source}
      property={{
        ...props.property,
        options: { ...props.property.options, choices: configured.choices },
      }}
    />
  );
}
