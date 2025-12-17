"use client";

import type { BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationOptionsModel } from "@/sdk/Api";

import { useTranslation } from "react-i18next";

import OptionsCard from "./OptionsCard";
import BooleanOptions from "./BooleanOptions";

import { SubjectLabels } from "@/pages/deprecated/synchronization-options/models";
import { enhancerIds } from "@/sdk/constants";
import EnhancerOptions from "@/pages/deprecated/synchronization-options/components/EnhancerOptions";
import { NumberInput } from "@/components/bakaui";

type Options =
  BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationOptionsModel;

type Props = {
  options?: Options;
  onChange?: (options: Options) => any;
};
const GlobalOptions = ({ options, onChange }: Props) => {
  const { t } = useTranslation();

  const patchOptions = (patches: Partial<Options>) => {
    const newOptions = {
      ...options,
      ...patches,
    };

    onChange?.(newOptions);
  };

  return (
    <OptionsCard header={t<string>("Global")}>
      <div className={"flex justify-end"}>
        <NumberInput
          isClearable
          className={"w-[360px]"}
          description={t<string>(
            "To reduce synchronization time, we will, by default, use 40% of your CPU's maximum thread count (rounded down) for syncing the media library",
          )}
          label={t<string>("Max threads")}
          minValue={1}
          size={"sm"}
          value={options?.maxThreads}
          onClear={() => {
            patchOptions({ maxThreads: undefined });
          }}
          onValueChange={(maxThreads) => {
            patchOptions({ maxThreads });
          }}
        />
      </div>
      <div />
      <BooleanOptions
        isSelected={options?.deleteResourcesWithUnknownMediaLibrary}
        subject={t<string>(SubjectLabels.DeleteResourcesWithUnknownMediaLibrary)}
        onSelect={(isSelected) =>
          patchOptions({ deleteResourcesWithUnknownMediaLibrary: isSelected })
        }
      />
      <BooleanOptions
        isSelected={options?.deleteResourcesWithUnknownPath}
        subject={t<string>(SubjectLabels.DeleteResourcesWithUnknownPath)}
        onSelect={(isSelected) => patchOptions({ deleteResourcesWithUnknownPath: isSelected })}
      />

      {enhancerIds.map((e) => {
        return (
          <EnhancerOptions
            enhancer={{
              id: e.value,
              name: e.label,
            }}
            options={options?.enhancerOptionsMap?.[e.value]}
            onChange={(o) =>
              patchOptions({
                enhancerOptionsMap: {
                  ...options?.enhancerOptionsMap,
                  [e.value]: o,
                },
              })
            }
          />
        );
      })}
    </OptionsCard>
  );
};

GlobalOptions.displayName = "GlobalOptions";

export default GlobalOptions;
