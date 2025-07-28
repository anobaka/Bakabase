"use client";

import type { BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationEnhancerOptions } from "@/sdk/Api";

import { useTranslation } from "react-i18next";

import BooleanOptions from "@/pages/synchronization-options/components/BooleanOptions";
import { Chip } from "@/components/bakaui";
import { EnhancerIcon } from "@/components/Enhancer";
import { SubjectLabels } from "@/pages/synchronization-options/models";

type Options =
  BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationEnhancerOptions;

type Props = {
  enhancer: { id: number; name: string };
  options?: Options;
  onChange?: (options: Options) => void;
  subject?: any;
  isSecondary?: boolean;
};
const EnhancerOptions = ({
  enhancer,
  options,
  onChange,
  subject,
  isSecondary,
}: Props) => {
  const { t } = useTranslation();
  const enhancerChip = (
    <Chip radius={"sm"} size={"sm"} variant={"flat"}>
      <div className={"flex items-center gap-1"}>
        {t<string>("Enhancer")}
        <EnhancerIcon id={enhancer.id} />
        {t<string>(enhancer.name)}
      </div>
    </Chip>
  );

  const patchOptions = (patches: Partial<Options>) => {
    const newOptions = {
      ...options,
      ...patches,
    };

    onChange?.(newOptions);
  };

  return (
    <>
      <BooleanOptions
        isSecondary={isSecondary}
        isSelected={options?.reApply}
        subject={
          subject ?? (
            <>
              {enhancerChip}
              {t<string>(SubjectLabels.EnhancerReApply)}
            </>
          )
        }
        onSelect={(isSelected) => {
          patchOptions({ reApply: isSelected });
        }}
      />
      <BooleanOptions
        isSecondary={isSecondary}
        isSelected={options?.reEnhance}
        subject={
          subject ?? (
            <>
              {enhancerChip}
              {t<string>(SubjectLabels.EnhancerReEnhance)}
            </>
          )
        }
        onSelect={(isSelected) => {
          patchOptions({ reEnhance: isSelected });
        }}
      />
    </>
  );
};

EnhancerOptions.displayName = "EnhancerOptions";

export default EnhancerOptions;
