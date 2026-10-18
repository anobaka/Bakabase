import type { Props } from "@/components/Property/components/PropertyValueRenderer";
import type { IChoice } from "@/components/Property/models";
import type { ManagementSource } from "@/features/management/api";

import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import MediaLibraryFilterValue from "./MediaLibraryFilterValue";

import { sourceKey } from "@/features/management/api";
import { canReadSourceMetadata } from "@/features/management/metadata";
import {
  createResourceApi,
  subscribeResourceOwnerChanges,
} from "@/features/management/resourceApi";
import { CatalogSourceStatus } from "@/features/management/ownedCatalog";
import DeviceDot from "@/features/management/DeviceDot";
import {
  mediaLibraryChoiceValue,
  parseMediaLibraryChoiceValue,
} from "@/features/management/mediaLibraryFilter";
import {
  deserializeStandardValue,
  serializeStandardValue,
} from "@/components/StandardValue/helpers";
import { getDbValueType } from "@/components/Property/PropertySystem";
import { PropertyPool, ResourceProperty, SearchOperation } from "@/sdk/constants";

type Choice = IChoice & { owner?: ManagementSource };
type UnifiedProps = Props & { sources: ManagementSource[]; operation?: SearchOperation };

/** One normal choice editor; only its catalog and IDs are composed across devices. */
export default function UnifiedMediaLibraryFilterValue({
  sources,
  operation = SearchOperation.In,
  ...props
}: UnifiedProps) {
  const { t } = useTranslation();
  const [catalogs, setCatalogs] = useState(new Map<string, IChoice[]>());
  const [errors, setErrors] = useState(new Map<string, string>());
  const [loading, setLoading] = useState(false);
  const [reload, setReload] = useState(0);
  const sourcesRef = useRef(sources);

  sourcesRef.current = sources;
  // Progress ticks do not reload choices; catalog changes and restored access do.
  const revision = JSON.stringify(
    sources.map((source) => [
      source.serverId,
      source.libraryEpoch,
      canReadSourceMetadata(source),
      source.metadata?.data.activeGeneration,
      source.metadata?.data.checkpoint?.catalogRevision,
    ]),
  );

  useEffect(() => {
    let cancelled = false;
    const readable = sourcesRef.current.filter(canReadSourceMetadata);
    const unsubscribe = readable.map((source) =>
      subscribeResourceOwnerChanges(source, () => setReload((value) => value + 1)),
    );
    const load = async () => {
      setLoading(readable.length > 0);
      const next = new Map<string, IChoice[]>();
      const failures = new Map<string, string>();

      for (let offset = 0; offset < readable.length; offset += 4) {
        await Promise.all(
          readable.slice(offset, offset + 4).map(async (source) => {
            try {
              const response = await createResourceApi(source).resource.getFilterValueProperty({
                propertyPool: PropertyPool.Internal,
                propertyId: ResourceProperty.MediaLibraryV2Multi,
                operation,
              });

              if (response.code) throw new Error(response.message || String(response.code));
              next.set(sourceKey(source), response.data?.options?.choices ?? []);
            } catch (error) {
              failures.set(sourceKey(source), String(error));
            }
          }),
        );
        if (cancelled) return;
      }
      if (!cancelled) {
        setCatalogs(next);
        setErrors(failures);
        setLoading(false);
      }
    };

    void load();

    return () => {
      cancelled = true;
      unsubscribe.forEach((stop) => stop());
    };
  }, [revision, operation, reload]);

  const valueType = getDbValueType(props.property.type);
  const legacyOwner = props.property.owner ?? sources.find((source) => source.isLocal);
  const selected = useMemo(() => {
    const value = deserializeStandardValue(props.dbValue ?? null, valueType);
    const values: string[] = Array.isArray(value) ? value : value == null ? [] : [String(value)];

    return values.map((id) =>
      parseMediaLibraryChoiceValue(id) || !legacyOwner
        ? id
        : mediaLibraryChoiceValue(legacyOwner, id),
    );
  }, [props.dbValue, valueType, legacyOwner?.serverId, legacyOwner?.libraryEpoch]);
  const dbValue = serializeStandardValue(
    Array.isArray(deserializeStandardValue(props.dbValue ?? null, valueType))
      ? selected
      : selected[0],
    valueType,
  );
  const choices = useMemo<Choice[]>(() => {
    const result: Choice[] = [];

    for (const source of sources) {
      if (!canReadSourceMetadata(source)) continue;
      for (const choice of catalogs.get(sourceKey(source)) ?? []) {
        result.push({
          ...choice,
          value: mediaLibraryChoiceValue(source, choice.value),
          owner: source,
        });
      }
    }
    const known = new Set(result.map((choice) => choice.value));
    const oldLabels = deserializeStandardValue(props.bizValue ?? null, valueType);
    const labels: string[] = Array.isArray(oldLabels)
      ? oldLabels
      : oldLabels == null
        ? []
        : [String(oldLabels)];

    // Keep missing selections removable. Never map an old device's numeric ID onto another device.
    selected.forEach((value, index) => {
      if (known.has(value)) return;
      const identity = parseMediaLibraryChoiceValue(value);
      const source =
        identity &&
        sources.find(
          (candidate) =>
            candidate.serverId === identity.owner.serverId &&
            candidate.libraryEpoch === identity.owner.libraryEpoch,
        );

      result.push({
        value,
        label: labels[index] ?? `#${identity?.id ?? value}`,
        owner:
          source ??
          (identity
            ? {
                ...identity.owner,
                isLocal: false,
                available: false,
                contractVersion: 1,
                name: t("management.unavailableDevice", { id: identity.owner.serverId }),
              }
            : undefined),
      });
    });

    return result;
  }, [sources, catalogs, selected, props.bizValue, props.property.options, valueType, t]);
  const byValue = new Map(choices.map((choice) => [choice.value, choice]));
  const property = {
    ...props.property,
    id: ResourceProperty.MediaLibraryV2Multi,
    pool: PropertyPool.Internal,
    owner: undefined,
    unifiedMediaLibraries: true,
    options: { ...props.property.options, choices },
  };
  // ChoiceValueRenderer caches its catalog while open. A catalog refresh must replace that cache,
  // while the selection remains controlled by the unchanged filter value.
  const choicesKey = JSON.stringify(
    choices.map(({ value, label, color }) => [value, label, color]),
  );

  return (
    <MediaLibraryFilterValue
      {...props}
      key={choicesKey}
      qualified
      bizValue={undefined}
      dbValue={dbValue}
      optionsDescription={
        <CatalogSourceStatus errors={errors} loading={loading} sources={sources} />
      }
      property={property}
      renderOptionExtra={(option) => {
        const owner = byValue.get(option.value)?.owner;

        return owner && !owner.isLocal ? (
          <DeviceDot
            isSource
            className="ml-1.5 h-2 w-2"
            name={owner.name}
            serverId={owner.serverId}
          />
        ) : null;
      }}
    />
  );
}
