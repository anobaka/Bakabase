"use client";

import type { IdName } from "@/pages/synchronization-options/models";
import type { BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationMediaLibraryOptions } from "@/sdk/Api";

import { useTranslation } from "react-i18next";

import OptionsCard from "./OptionsCard";

import { SubjectLabels } from "@/pages/synchronization-options/models";
import BooleanOptions from "@/pages/synchronization-options/components/BooleanOptions";
import EnhancerOptions from "@/pages/synchronization-options/components/EnhancerOptions";

type Options =
  BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationMediaLibraryOptions;

type MediaLibrary = IdName & { enhancers?: IdName[] };

type Props = {
  mediaLibrary: MediaLibrary;
  onChange?: (options: Options) => any;
  options?: Options;
};
const MediaLibraryOptions = ({ mediaLibrary, onChange, options }: Props) => {
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
    <OptionsCard header={mediaLibrary.name}>
      <BooleanOptions
        isSelected={options?.deleteResourcesWithUnknownPath}
        subject={t<string>(SubjectLabels.DeleteResourcesWithUnknownPath)}
        onSelect={(isSelected) =>
          patchOptions({ deleteResourcesWithUnknownPath: isSelected })
        }
      />
      <div />
      {mediaLibrary.enhancers?.map((e) => {
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
          </>
        );
      })}
    </OptionsCard>
  );
};

MediaLibraryOptions.displayName = "MediaLibraryOptions";

export default MediaLibraryOptions;
