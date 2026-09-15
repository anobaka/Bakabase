"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { IProperty } from "@/components/Property/models";
import type { EnhancerDescriptor } from "@/components/EnhancerSelectorV2/models";
import type {
  BakabaseAbstractionsModelsDomainEnhancerFullOptions,
  BakabaseAbstractionsModelsDomainResourceProfilePropertyOptions,
  BakabaseAbstractionsModelsDomainPropertyKeyWithScopePriority,
} from "@/sdk/Api";
import type { EnhancerId } from "@/sdk/constants";

import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  AiOutlineClose,
  AiOutlineGlobal,
  AiOutlineLink,
  AiOutlinePlus,
  AiOutlineSearch,
} from "react-icons/ai";

import ScopePriorityEditor from "../ScopePriorityEditor";
import GlobalScopePriorityModal from "../GlobalScopePriorityModal";
import { enhancerIdToScope } from "../EnhancementConfigPanel/utils";

import { Button, Input, Modal, Tooltip } from "@/components/bakaui";
import { PropertyPool, PropertyTypeLabel, PropertyValueScope } from "@/sdk/constants";
import { PropertyLabel } from "@/components/Property";
import PropertySelector from "@/components/PropertySelector";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { useResourceOptionsStore } from "@/stores/options";

type PropertyRef = BakabaseAbstractionsModelsDomainPropertyKeyWithScopePriority;
type PropertyOptions = BakabaseAbstractionsModelsDomainResourceProfilePropertyOptions;
type Props = {
  propertyOptions?: PropertyOptions;
  allProperties: IProperty[];
  enhancerOptions: BakabaseAbstractionsModelsDomainEnhancerFullOptions[];
  enhancerDescriptors: EnhancerDescriptor[];
  onSubmit?: (options: PropertyOptions | undefined) => unknown | Promise<unknown>;
} & DestroyableProps;

const propertyKey = (property: { pool?: number; id?: number }) => `${property.pool}:${property.id}`;

export default function PropertyPoolModal({
  propertyOptions,
  allProperties,
  enhancerOptions,
  onSubmit,
  onDestroyed,
}: Props) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const globalPriority = useResourceOptionsStore((store) => store.data?.propertyValueScopePriority);
  const [propertyRefs, setPropertyRefs] = useState<PropertyRef[]>(() =>
    (propertyOptions?.properties ?? []).map((ref) => ({
      ...ref,
      scopePriority: ref.scopePriority?.slice(),
    })),
  );
  const [keyword, setKeyword] = useState("");
  const [saving, setSaving] = useState(false);
  const propertyMap = useMemo(
    () => new Map(allProperties.map((property) => [propertyKey(property), property])),
    [allProperties],
  );

  const getAvailableScopes = (ref: PropertyRef): PropertyValueScope[] => {
    const available = [PropertyValueScope.Manual, PropertyValueScope.Synchronization];

    for (const option of enhancerOptions) {
      if (
        !option.targetOptions?.some(
          (target) => target.propertyPool === ref.pool && target.propertyId === ref.id,
        )
      )
        continue;
      const scope = enhancerIdToScope(option.enhancerId as EnhancerId);

      if (scope !== undefined && !available.includes(scope)) available.push(scope);
    }

    return [
      ...new Set([
        ...(globalPriority ?? []).filter((scope) => available.includes(scope)),
        ...available,
      ]),
    ];
  };

  const addProperties = () =>
    createPortal(PropertySelector, {
      title: t<string>("resourceProfile.propertyPool.title"),
      v2: true,
      pool: PropertyPool.Custom,
      multiple: true,
      selection: propertyRefs
        .filter((ref) => ref.pool === PropertyPool.Custom && propertyMap.has(propertyKey(ref)))
        .map((ref) => ({ pool: ref.pool, id: ref.id })),
      onSubmit: async (selected: IProperty[]) => {
        setPropertyRefs((current) => {
          const selectedKeys = new Set(selected.map(propertyKey));
          // The custom-property selector cannot represent missing or non-custom refs.
          // Keep those until the user explicitly unlinks them in this list.
          const next = current.filter(
            (ref) =>
              selectedKeys.has(propertyKey(ref)) ||
              ref.pool !== PropertyPool.Custom ||
              !propertyMap.has(propertyKey(ref)),
          );
          const retainedKeys = new Set(next.map(propertyKey));

          for (const property of selected) {
            if (!retainedKeys.has(propertyKey(property))) {
              next.push({ pool: property.pool, id: property.id });
              retainedKeys.add(propertyKey(property));
            }
          }

          return next;
        });
      },
    });

  const changePriority = (key: string, scopePriority: PropertyValueScope[] | null) =>
    setPropertyRefs((current) =>
      current.map((ref) =>
        propertyKey(ref) === key ? { ...ref, scopePriority: scopePriority ?? undefined } : ref,
      ),
    );
  const submit = async () => {
    setSaving(true);
    try {
      await onSubmit?.(
        propertyRefs.length ? { ...propertyOptions, properties: propertyRefs } : undefined,
      );
    } finally {
      setSaving(false);
    }
  };
  const filteredRefs = propertyRefs.filter((ref) => {
    const property = propertyMap.get(propertyKey(ref));
    const type =
      property?.type !== undefined
        ? t<string>(`PropertyType.${PropertyTypeLabel[property.type]}`)
        : "";
    const name = property?.name ?? t<string>("resourceProfile.propertyPool.unknownProperty");

    return `${name} ${type} ${ref.id}`
      .toLocaleLowerCase()
      .includes(keyword.trim().toLocaleLowerCase());
  });

  return (
    <Modal
      defaultVisible
      classNames={{ base: "max-w-4xl" }}
      footer={{
        actions: ["ok", "cancel"],
        okProps: { children: t<string>("common.action.save") },
        cancelProps: { children: t<string>("common.action.cancel"), isDisabled: saving },
      }}
      hideCloseButton={saving}
      isDismissable={!saving}
      isKeyboardDismissDisabled={saving}
      size="3xl"
      title={t<string>("resourceProfile.propertyPool.title")}
      onDestroyed={onDestroyed}
      onOk={submit}
    >
      <p className="text-sm leading-6 text-default-500">
        {t<string>("resourceProfile.propertyPool.description")}
      </p>
      <div className="sticky top-0 z-10 my-2 flex flex-wrap items-center gap-2 bg-content1 py-2">
        <Input
          aria-label={t<string>("resourceProfile.propertyPool.search")}
          className="w-full min-w-0 sm:w-auto sm:max-w-xs sm:flex-1"
          isDisabled={saving}
          placeholder={t<string>("resourceProfile.propertyPool.search")}
          size="sm"
          startContent={<AiOutlineSearch className="shrink-0 text-default-400" />}
          value={keyword}
          onValueChange={setKeyword}
        />
        <span className="mr-auto text-xs tabular-nums text-default-400">
          {t<string>("resourceProfile.propertyPool.count", {
            shown: filteredRefs.length,
            total: propertyRefs.length,
          })}
        </span>
        <Button
          color="primary"
          isDisabled={saving}
          size="sm"
          startContent={<AiOutlinePlus />}
          onPress={addProperties}
        >
          {t<string>("resourceProfile.propertyPool.addProperty")}
        </Button>
      </div>
      {propertyRefs.length === 0 ? (
        <div className="flex min-h-48 flex-col items-center justify-center gap-3 text-default-500">
          <AiOutlineLink aria-hidden className="text-3xl" />
          <p className="text-sm">{t<string>("resourceProfile.propertyPool.empty")}</p>
        </div>
      ) : filteredRefs.length === 0 ? (
        <p className="py-10 text-center text-sm text-default-500">
          {t<string>("resourceProfile.propertyPool.noMatches")}
        </p>
      ) : (
        <div
          aria-label={t<string>("resourceProfile.propertyPool.listLabel")}
          className="divide-y divide-default-100"
          role="list"
        >
          {filteredRefs.map((ref) => {
            const key = propertyKey(ref);
            const property = propertyMap.get(key);
            const name =
              property?.name ?? t<string>("resourceProfile.propertyPool.unknownProperty");

            return (
              <div
                key={key}
                className="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-4 gap-y-2 py-3 sm:grid-cols-[minmax(0,1fr)_minmax(12rem,1fr)_auto]"
                role="listitem"
              >
                <div
                  className="min-w-0 break-words [&>div]:max-w-full [&>div>span]:truncate"
                  title={name}
                >
                  {property ? (
                    <PropertyLabel property={property} />
                  ) : (
                    <span className="text-sm text-warning-700">{name}</span>
                  )}
                  <div className="mt-1 text-xs text-default-400">
                    {property
                      ? t<string>(`PropertyType.${PropertyTypeLabel[property.type]}`)
                      : t<string>("resourceProfile.propertyPool.missingHint", {
                          id: ref.id,
                          pool: t<string>(`PropertyPool.${PropertyPool[ref.pool]}`),
                        })}
                  </div>
                </div>
                <div className="col-start-1 row-start-2 min-w-0 sm:col-start-auto sm:row-start-auto">
                  <ScopePriorityEditor
                    availableScopes={getAvailableScopes(ref)}
                    isDisabled={saving}
                    value={ref.scopePriority ?? null}
                    onChange={(priority) => changePriority(key, priority)}
                  />
                </div>
                <Tooltip content={t<string>("resourceProfile.propertyPool.unlink")}>
                  <Button
                    isIconOnly
                    aria-label={t<string>("resourceProfile.propertyPool.unlinkNamed", {
                      name,
                      id: ref.id,
                    })}
                    className="col-start-2 row-start-1 text-default-400 sm:col-start-auto sm:row-start-auto"
                    isDisabled={saving}
                    size="sm"
                    variant="light"
                    onPress={() =>
                      setPropertyRefs((current) =>
                        current.filter((item) => propertyKey(item) !== key),
                      )
                    }
                  >
                    <AiOutlineClose className="text-base" />
                  </Button>
                </Tooltip>
              </div>
            );
          })}
        </div>
      )}
      <div className="mt-2 flex flex-wrap items-center justify-between gap-3 border-t border-default-100 pt-3">
        <p className="max-w-lg text-xs leading-5 text-default-500">
          {t<string>("resourceProfile.propertyPool.unlinkHint")}
        </p>
        <Button
          isDisabled={saving}
          size="sm"
          startContent={<AiOutlineGlobal />}
          variant="light"
          onPress={() => createPortal(GlobalScopePriorityModal, {})}
        >
          {t<string>("resourceProfile.propertyPool.globalScopePriority")}
        </Button>
      </div>
    </Modal>
  );
}
