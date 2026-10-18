"use client";

import type { IProperty } from "@/components/Property/models";
import type { SerializedStandardValue } from "@/components/StandardValue";

import { useEffect, useRef, useState } from "react";

import { Button } from "@/components/bakaui";
import { getBizValueType, isReferenceValueType } from "@/components/Property/PropertySystem";
import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";
import { serializeStandardValue } from "@/components/StandardValue/helpers";
import BApi from "@/sdk/BApi";

type Props = {
  property: IProperty;
  /** Path marks store serialized business values, including reference labels. */
  value?: SerializedStandardValue | number | boolean;
  onChange: (value?: SerializedStandardValue) => void;
  t: (key: string) => string;
};

type Resolution = {
  propertyKey: string;
  value?: SerializedStandardValue;
  dbValue?: SerializedStandardValue;
  failed?: boolean;
};

const FixedPropertyValueEditor = ({ property, value: storedValue, onChange, t }: Props) => {
  const propertyKey = `${property.pool}:${property.id}:${property.type}`;
  const isReference = isReferenceValueType(property.type);
  // Older marks may contain JSON scalar values instead of serialized strings.
  // Normalize only at the editor boundary, preserving the stored value on load.
  const value =
    typeof storedValue === "string" || storedValue == null
      ? storedValue
      : serializeStandardValue(storedValue, getBizValueType(property.type));
  const [resolution, setResolution] = useState<Resolution>();
  const latestResolution = useRef<Resolution>();
  const [retry, setRetry] = useState(0);

  useEffect(() => {
    if (!isReference || !value) return;
    if (
      latestResolution.current?.propertyKey === propertyKey &&
      latestResolution.current.value === value &&
      !latestResolution.current.failed
    )
      return;

    const controller = new AbortController();
    let active = true;

    // Resolve business labels through the same property descriptor used by the
    // server. IDs, labels, tags and escaped multilevel paths are not interchangeable.
    BApi.property
      .getPropertyDbValue(
        property.pool,
        property.id,
        { bizValue: value },
        {
          signal: controller.signal,
          showErrorToast: false,
        },
      )
      .then((response) => {
        if (!active) return;
        if (response.code) throw new Error(response.message);
        const next = { propertyKey, value, dbValue: response.data ?? undefined };

        latestResolution.current = next;
        setResolution(next);
      })
      .catch(() => {
        if (!active) return;
        const next = { propertyKey, value, failed: true };

        latestResolution.current = next;
        setResolution(next);
      });

    return () => {
      active = false;
      controller.abort();
    };
  }, [property.pool, property.id, propertyKey, isReference, value, retry]);

  const currentResolution =
    resolution?.propertyKey === propertyKey && resolution.value === value ? resolution : undefined;

  if (isReference && value && !currentResolution) {
    return <span className="text-sm text-default-400">{t("common.state.loading")}</span>;
  }

  if (isReference && value && currentResolution?.failed) {
    return (
      <div className="flex items-center gap-2 text-sm text-danger" role="alert">
        {t("pathMarkConfig.error.loadFixedValue")}
        <Button size="sm" variant="light" onPress={() => setRetry((current) => current + 1)}>
          {t("common.action.retry")}
        </Button>
      </div>
    );
  }

  return (
    <PropertyValueRenderer
      key={propertyKey}
      isEditing
      bizValue={value}
      dbValue={isReference ? currentResolution?.dbValue : value}
      property={property}
      size="sm"
      onValueChange={(dbValue, bizValue) => {
        // Keep the editor's selection locally, while persisting only business
        // values so the path mark remains valid when option IDs change.
        const next = { propertyKey, value: bizValue, dbValue };

        latestResolution.current = next;
        setResolution(next);
        onChange(bizValue);
      }}
    />
  );
};

export default FixedPropertyValueEditor;
