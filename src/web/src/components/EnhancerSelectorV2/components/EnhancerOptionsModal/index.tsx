"use client";

import type { EnhancerDescriptor } from "@/components/EnhancerSelectorV2/models";
import type {
  EnhancerFullOptions,
  RegexEnhancerFullOptions,
} from "@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/models";
import type { IProperty } from "@/components/Property/models";
import type { DestroyableProps } from "@/components/bakaui/types";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";
import { useUpdate } from "react-use";
import _ from "lodash";

import DynamicTargets from "./components/DynamicTargets";
import FixedTargets from "./components/FixedTargets";
import RegexEnhancerOptions from "./components/RegexEnhancerOptions";

import { Modal } from "@/components/bakaui";
import { EnhancerId, PropertyPool } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BriefEnhancer from "@/components/Chips/Enhancer/BriefEnhancer";

type Props<TOptions extends EnhancerFullOptions> = {
  enhancer: EnhancerDescriptor;
  options?: TOptions;
  onSubmit?: (options: EnhancerFullOptions) => any;
} & DestroyableProps;

export default function EnhancerOptionsModal<T extends EnhancerFullOptions>({
  enhancer,
  options: propsOptions,
  onSubmit,
  onDestroyed,
}: Props<T>) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const forceUpdate = useUpdate();

  const [options, setOptions] = useState<EnhancerFullOptions>(
    propsOptions ?? {},
  );
  const [propertyMap, setPropertyMap] = useState<{
    [key in PropertyPool]?: Record<number, IProperty>;
  }>({});

  const init = async () => {
    await loadAllProperties();
  };

  useEffect(() => {
    init();
  }, []);

  const loadAllProperties = async () => {
    const psr =
      (await BApi.property.getPropertiesByPool(PropertyPool.All)).data || [];
    const ps = _.mapValues(
      _.groupBy(psr, (x) => x.pool),
      (v) => _.keyBy(v, (x) => x.id),
    );

    setPropertyMap(ps);
  };

  console.log(options);

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["cancel", "ok"],
      }}
      size={"xl"}
      title={
        <div className={"flex items-center gap-x-2"}>
          {t<string>("Configure enhancer")}
          <BriefEnhancer enhancer={enhancer} />
        </div>
      }
      onDestroyed={onDestroyed}
      onOk={() => onSubmit?.(options)}
    >
      {enhancer.id == EnhancerId.Regex ? (
        <RegexEnhancerOptions
          enhancer={enhancer}
          options={options as RegexEnhancerFullOptions}
          propertyMap={propertyMap}
          onChange={(options) => {
            setOptions({ ...options });
          }}
          onPropertyChanged={loadAllProperties}
        />
      ) : (
        options && (
          <div className={"flex flex-col gap-y-4"}>
            <FixedTargets
              enhancer={enhancer}
              optionsList={options.targetOptions}
              propertyMap={propertyMap}
              onChange={(list) => {
                setOptions({
                  ...options,
                  targetOptions: list,
                });
              }}
              onPropertyChanged={loadAllProperties}
            />
            <DynamicTargets
              enhancer={enhancer}
              optionsList={options.targetOptions}
              propertyMap={propertyMap}
              onChange={(list) => {
                setOptions({
                  ...options,
                  targetOptions: list,
                });
              }}
              onPropertyChanged={loadAllProperties}
            />
          </div>
        )
      )}
    </Modal>
  );
}
