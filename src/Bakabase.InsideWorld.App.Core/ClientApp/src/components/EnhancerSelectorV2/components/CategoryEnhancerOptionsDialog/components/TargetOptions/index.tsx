import { useState } from 'react';
import IntegrateWithAlias from './IntegrateWithAlias';
import AutoMatchMultilevelString from './AutoMatchMultilevelString';
import type {
  EnhancerFullOptions,
  EnhancerTargetFullOptions,
} from '@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/models';
import { EnhancerTargetOptionsItem } from '@/sdk/constants';

interface IProps {
  options?: EnhancerTargetFullOptions;
  optionsItems?: EnhancerTargetOptionsItem[];
  onChange?: (options: EnhancerTargetFullOptions) => void;
}

export default ({ options: propsOptions, optionsItems, onChange }: IProps) => {
  const [options, setOptions] = useState<EnhancerTargetFullOptions>(propsOptions ?? {} as EnhancerTargetFullOptions);

  console.log(propsOptions);

  const patchOptions = (changes: Partial<EnhancerTargetFullOptions>, triggerChange: boolean = true) => {
    const no = {
      ...options,
      ...changes,
    };
    setOptions(no);
    if (triggerChange) {
      onChange?.(no);
    }
  };

  return (
    <>
      {optionsItems?.map((item, index) => {
        switch (item) {
          case EnhancerTargetOptionsItem.IntegrateWithAlias:
            return (
              <IntegrateWithAlias
                integrateWithAlias={options.integrateWithAlias ?? false}
                onChange={o => patchOptions({ integrateWithAlias: o })}
              />
            );
          case EnhancerTargetOptionsItem.AutoMatchMultilevelString:
            return (
              <AutoMatchMultilevelString
                autoMatch={options.autoMatchMultilevelString ?? false}
                onChange={o => patchOptions({ autoMatchMultilevelString: o })}
              />
            );
        }
      })}
    </>
  );
};
