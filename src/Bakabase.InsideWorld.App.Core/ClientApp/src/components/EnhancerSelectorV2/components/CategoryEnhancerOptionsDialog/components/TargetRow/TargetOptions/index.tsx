import { useState } from 'react';
import IntegrateWithAlias from './IntegrateWithAlias';
import AutoMatchMultilevelString from './AutoMatchMultilevelString';
import AutoGenerateProperties from './AutoGenerateProperties';
import type {
  EnhancerFullOptions,
  EnhancerTargetFullOptions,
} from '@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/models';
import { EnhancerTargetOptionsItem } from '@/sdk/constants';

interface IProps {
  options?: Partial<EnhancerTargetFullOptions>;
  optionsItems?: EnhancerTargetOptionsItem[];
  onChange?: (options: Partial<EnhancerTargetFullOptions>) => void;
}

export default ({ options: propsOptions, optionsItems, onChange }: IProps) => {
  const [options, setOptions] = useState<Partial<EnhancerTargetFullOptions>>(propsOptions ?? {});

  // console.log(propsOptions);

  const patchOptions = (patches: Partial<EnhancerTargetFullOptions>, triggerChange: boolean = true) => {
    const no = {
      ...options,
      ...patches,
    };
    setOptions(no);
    if (triggerChange) {
      onChange?.(patches);
    }
  };

  const finalOptions = propsOptions ?? options;

  return (
    <>
      {optionsItems?.map((item, index) => {
        switch (item) {
          case EnhancerTargetOptionsItem.IntegrateWithAlias:
            return (
              <IntegrateWithAlias
                key={item}
                integrateWithAlias={finalOptions.integrateWithAlias ?? false}
                onChange={o => patchOptions({ integrateWithAlias: o })}
              />
            );
          case EnhancerTargetOptionsItem.AutoMatchMultilevelString:
            return (
              <AutoMatchMultilevelString
                key={item}
                autoMatch={finalOptions.autoMatchMultilevelString ?? false}
                onChange={o => patchOptions({ autoMatchMultilevelString: o })}
              />
            );
          case EnhancerTargetOptionsItem.AutoGenerateProperties:
            return (
              <AutoGenerateProperties
                key={item}
                autoGenerateProperties={finalOptions.autoGenerateProperties ?? false}
                onChange={o => patchOptions({ autoGenerateProperties: o })}
              />
            );
        }
      })}
    </>
  );
};
