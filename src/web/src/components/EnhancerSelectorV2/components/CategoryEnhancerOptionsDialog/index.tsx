"use client";

import type { EnhancerDescriptor } from "@/components/EnhancerSelectorV2/models";
import type { EnhancerFullOptions } from "@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/models";
import type { IProperty } from "@/components/Property/models";
import type { DestroyableProps } from "@/components/bakaui/types";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";
import { useUpdate } from "react-use";

import DynamicTargets from "./components/DynamicTargets";
import FixedTargets from "./components/FixedTargets";
import RegexEnhancerOptions from "./components/RegexEnhancerOptions";
import TranslationOptionsSection from "../TranslationOptionsSection";

import { Divider, Modal } from "@/components/bakaui";
import { createPortalOfComponent } from "@/components/utils";
import {
  CategoryAdditionalItem,
  EnhancerId,
  PropertyPool,
} from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import {
  ExHentaiConfigPanel,
  ExHentaiConfigField,
  DLsiteConfigPanel,
  DLsiteConfigField,
  BangumiConfigPanel,
  BangumiConfigField,
} from "@/components/ThirdPartyConfig";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

/** Sanitize targetOptions on load: treat pool=0/id=0 (C# default for unbound) as undefined */
const sanitizeOptions = (opts?: EnhancerFullOptions): EnhancerFullOptions => {
  if (!opts) return {};
  return {
    ...opts,
    targetOptions: opts.targetOptions?.map((to) => {
      if ((to.propertyPool != null && to.propertyPool <= 0) || (to.propertyId != null && to.propertyId <= 0)) {
        return { ...to, propertyPool: undefined, propertyId: undefined };
      }
      return to;
    }),
  };
};

type IProps = {
  enhancer: EnhancerDescriptor;
  categoryId: number;
} & DestroyableProps;

const CategoryEnhancerOptionsDialog = ({
  enhancer,
  categoryId,
  onDestroyed,
}: IProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const forceUpdate = useUpdate();

  const [category, setCategory] = useState<{
    id: number;
    name: string;
    customPropertyIds: number[];
  }>({
    id: 0,
    name: "",
    customPropertyIds: [],
  });

  const [options, setOptions] = useState<EnhancerFullOptions>();
  const [propertyMap, setPropertyMap] = useState<{
    [key in PropertyPool]?: Record<number, IProperty>;
  }>({});

  useEffect(() => {
    // Load all data in parallel for faster modal open
    Promise.all([
      // @ts-ignore
      BApi.category.getCategory(categoryId, {
        additionalItems:
          CategoryAdditionalItem.EnhancerOptions |
          CategoryAdditionalItem.CustomProperties,
      }),
      BApi.property.getPropertiesByPool(PropertyPool.All),
    ]).then(([categoryRes, propertiesRes]) => {
      // Process category
      setCategory({
        id: categoryRes.data?.id ?? 0,
        name: categoryRes.data?.name ?? "",
        customPropertyIds: (categoryRes.data?.customProperties ?? []).map((p: any) => p.id!),
      });
      // @ts-ignore
      const ceo = (categoryRes.data?.enhancerOptions?.find(
        (x) => x.enhancerId == enhancer.id,
      )?.options ?? {}) as EnhancerFullOptions;
      setOptions(sanitizeOptions(ceo));

      // Process properties
      const psr = propertiesRes.data || [];
      const ps: { [key in PropertyPool]?: Record<number, IProperty> } =
        psr.reduce<{ [key in PropertyPool]?: Record<number, IProperty> }>(
          (s, t) => {
            const pt = t.pool!;
            s[pt] ??= {};
            // @ts-ignore
            s[pt][t.id!] = t;
            return s;
          },
          {},
        );
      setPropertyMap(ps);
    });
  }, []);

  const loadCategory = async () => {
    // @ts-ignore
    const r = await BApi.category.getCategory(categoryId, {
      additionalItems: CategoryAdditionalItem.EnhancerOptions | CategoryAdditionalItem.CustomProperties,
    });
    setCategory({
      id: r.data?.id ?? 0,
      name: r.data?.name ?? "",
      customPropertyIds: (r.data?.customProperties ?? []).map((p: any) => p.id!),
    });
    // @ts-ignore
    const ceo = (r.data?.enhancerOptions?.find((x) => x.enhancerId == enhancer.id)?.options ?? {}) as EnhancerFullOptions;
    setOptions(sanitizeOptions(ceo));
  };

  const loadAllProperties = async () => {
    const psr = (await BApi.property.getPropertiesByPool(PropertyPool.All)).data || [];
    const ps: { [key in PropertyPool]?: Record<number, IProperty> } = psr.reduce<{ [key in PropertyPool]?: Record<number, IProperty> }>((s, t) => {
      const pt = t.pool!;
      s[pt] ??= {};
      // @ts-ignore
      s[pt][t.id!] = t;
      return s;
    }, {});
    setPropertyMap(ps);
  };

  const loadOptions = async () => {
    const { data } = await BApi.category.getCategoryEnhancerOptions(
      categoryId,
      enhancer.id,
    );
    setOptions(sanitizeOptions(data?.options));
  };

  console.log(options);

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["cancel"],
      }}
      size={"full"}
      title={t<string>(
        "enhancer.categoryOptions.title",
        {
          enhancerName: enhancer.name,
          categoryName: category.name,
        },
      )}
      onDestroyed={onDestroyed}
    >
      {/* Embed third-party account config for platform-linked enhancers */}
      {enhancer.id === EnhancerId.ExHentai && (
        <>
          <ExHentaiConfigPanel fields={[ExHentaiConfigField.Accounts]} showFooter={false} />
          <Divider className="my-4" />
        </>
      )}
      {enhancer.id === EnhancerId.DLsite && (
        <>
          <DLsiteConfigPanel fields={[DLsiteConfigField.Accounts]} showFooter={false} />
          <Divider className="my-4" />
        </>
      )}
      {enhancer.id === EnhancerId.Bangumi && (
        <>
          <BangumiConfigPanel fields={[BangumiConfigField.Accounts]} />
          <Divider className="my-4" />
        </>
      )}

      {enhancer.id == EnhancerId.Regex ? (
        <RegexEnhancerOptions
          category={category}
          enhancer={enhancer}
          options={options}
          propertyMap={propertyMap}
          onCategoryChanged={loadCategory}
          onPropertyChanged={loadAllProperties}
        />
      ) : (
        options && (
          <div className={"flex flex-col gap-y-4"}>
            <FixedTargets
              category={category}
              enhancer={enhancer}
              options={options}
              propertyMap={propertyMap}
              onCategoryChanged={loadCategory}
              onPropertyChanged={loadAllProperties}
            />
            <DynamicTargets
              category={category}
              enhancer={enhancer}
              options={options}
              propertyMap={propertyMap}
              onCategoryChanged={loadCategory}
              onPropertyChanged={loadAllProperties}
            />
            <TranslationOptionsSection
              value={options.translationOptions}
              onChange={async (translationOptions) => {
                const newOptions = { ...options, translationOptions };
                await BApi.category.patchCategoryEnhancerOptions(
                  categoryId,
                  enhancer.id,
                  { options: newOptions as any },
                );
                setOptions(newOptions);
              }}
            />
          </div>
        )
      )}
    </Modal>
  );
};

CategoryEnhancerOptionsDialog.show = (props: IProps) =>
  createPortalOfComponent(CategoryEnhancerOptionsDialog, props);

export default CategoryEnhancerOptionsDialog;
