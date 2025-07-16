"use client";

import type { IdName } from "@/pages/synchronization-options/models";
import type { BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationCategoryOptions } from "@/sdk/Api";

import { useTranslation } from "react-i18next";

import OptionsCard from "./OptionsCard";

import { SubjectLabels } from "@/pages/synchronization-options/models";
import BooleanOptions from "@/pages/synchronization-options/components/BooleanOptions";
import EnhancerOptions from "@/pages/synchronization-options/components/EnhancerOptions";
import DeprecatedChip from "@/components/Chips/DeprecatedChip";

type Options =
  BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationCategoryOptions;

type Category = {
  name: string;
  id: number;
  enhancers?: IdName[];
  mediaLibraries?: IdName[];
};

type Props = {
  category: Category;
  onChange?: (options: Options) => any;
  options?: Options;
};

/**
 * @deprecated
 */
const CategoryOptions = ({ category, onChange, options }: Props) => {
  const { t } = useTranslation();

  const patchOptions = (patches: Partial<Options>) => {
    const newOptions = {
      ...options,
      ...patches,
    };

    onChange?.(newOptions);
  };

  // console.log(options, mediaLibrariesOptionsMap);

  return (
    <OptionsCard
      header={
        <div className={"flex items-center gap-1 opacity-60"}>
          {category.name}
          <DeprecatedChip />
        </div>
      }
    >
      <BooleanOptions
        isSelected={options?.deleteResourcesWithUnknownPath}
        subject={t<string>(SubjectLabels.DeleteResourcesWithUnknownPath)}
        onSelect={(isSelected) =>
          patchOptions({ deleteResourcesWithUnknownPath: isSelected })
        }
      />
      <div />
      {category.mediaLibraries?.map((l) => {
        return (
          <BooleanOptions
            isSecondary
            isSelected={
              options?.mediaLibraryOptionsMap?.[l.id]
                ?.deleteResourcesWithUnknownPath
            }
            subject={l.name}
            onSelect={(isSelected) =>
              patchOptions({
                mediaLibraryOptionsMap: {
                  ...options?.mediaLibraryOptionsMap,
                  [l.id]: {
                    ...options?.mediaLibraryOptionsMap?.[l.id],
                    deleteResourcesWithUnknownPath: isSelected,
                  },
                },
              })
            }
          />
        );
      })}
      <div />
      {category.enhancers?.map((e) => {
        return (
          <>
            <EnhancerOptions
              enhancer={e}
              options={options?.enhancerOptionsMap?.[e.id]}
              onChange={(o) =>
                patchOptions({
                  enhancerOptionsMap: {
                    ...options?.enhancerOptionsMap,
                    [e.id]: o,
                  },
                })
              }
            />
            {category.mediaLibraries?.map((l) => {
              return (
                <EnhancerOptions
                  isSecondary
                  enhancer={e}
                  options={
                    options?.mediaLibraryOptionsMap?.[l.id]
                      ?.enhancerOptionsMap?.[e.id]
                  }
                  subject={l.name}
                  onChange={(o) =>
                    patchOptions({
                      mediaLibraryOptionsMap: {
                        ...options?.mediaLibraryOptionsMap,
                        [l.id]: {
                          ...options?.mediaLibraryOptionsMap?.[l.id],
                          enhancerOptionsMap: {
                            ...options?.mediaLibraryOptionsMap?.[l.id]
                              ?.enhancerOptionsMap,
                            [e.id]: o,
                          },
                        },
                      },
                    })
                  }
                />
              );
            })}
          </>
        );
      })}
    </OptionsCard>
  );
};

export default CategoryOptions;
