import type { Props } from "@/components/Property/components/PropertyValueRenderer";

import { useTranslation } from "react-i18next";
import { useCallback, useEffect, useMemo } from "react";

import { useFilterChoiceCounts } from "../../hooks/useFilterChoiceCounts";

import ChoiceResourceCount, { ChoiceResourceCountsStatus } from "./ChoiceResourceCount";
import styles from "./value.module.scss";

import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";
import { isReferenceValueType } from "@/components/Property/PropertySystem";
import {
  createDisabledChoiceKeysSource,
  useDisabledChoiceKeys,
} from "@/hooks/useDisabledChoiceKeys";
import { useReferenceValueSearch } from "@/hooks/useReferenceValueResourceCounts";

/** Adds resource-search behavior to the ordinary property value renderer. */
export default function FilterValueRenderer(props: Props) {
  return (
    <div className={styles.value}>
      {isReferenceValueType(props.property.type) ? (
        <FilterValueWithChoiceCounts {...props} />
      ) : (
        <PropertyValueRenderer {...props} />
      )}
    </div>
  );
}

function FilterValueWithChoiceCounts(props: Props) {
  const { t } = useTranslation();
  const search = useReferenceValueSearch();
  const {
    store,
    disabledKeys: unusedKeys,
    propertyKey,
    hasCounts,
  } = useFilterChoiceCounts(props.property, search);
  const explicitDisabledKeys = useDisabledChoiceKeys(props.disabledKeysSource, props.disabledKeys);
  const disabledKeys = useMemo(
    () =>
      !unusedKeys && !explicitDisabledKeys
        ? undefined
        : new Set([...(unusedKeys ?? []), ...(explicitDisabledKeys ?? [])]),
    [unusedKeys, explicitDisabledKeys],
  );
  const disabledKeysSource = useMemo(createDisabledChoiceKeysSource, [propertyKey]);

  useEffect(() => disabledKeysSource.publish(disabledKeys), [disabledKeysSource, disabledKeys]);

  // Editors keep the callback passed at open time. The business component subscribes to
  // a stable store itself, so neither the renderer nor its portal needs to know about counts.
  const renderOptionExtra = useCallback<NonNullable<Props["renderOptionExtra"]>>(
    (option) => (
      <>
        {props.renderOptionExtra?.(option)}
        <ChoiceResourceCount choiceId={option.value} store={store} />
      </>
    ),
    [props.renderOptionExtra, store],
  );
  const optionsDescription = (
    <>
      {props.optionsDescription}
      <ChoiceResourceCountsStatus store={store} />
    </>
  );

  return (
    <div
      title={
        hasCounts
          ? t(search ? "property.reference.filteredCountsHelp" : "property.reference.countsHelp")
          : undefined
      }
    >
      <PropertyValueRenderer
        {...props}
        disabledKeys={disabledKeys}
        disabledKeysSource={disabledKeysSource}
        optionsDescription={optionsDescription}
        renderOptionExtra={renderOptionExtra}
      />
      {optionsDescription}
    </div>
  );
}
