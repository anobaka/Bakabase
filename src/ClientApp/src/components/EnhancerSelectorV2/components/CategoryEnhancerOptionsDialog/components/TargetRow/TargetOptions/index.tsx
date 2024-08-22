import { useEffect, useState } from 'react';
import IntegrateWithAlias from './IntegrateWithAlias';
import AutoMatchMultilevelString from './AutoMatchMultilevelString';
import AutoGenerateProperties from './AutoGenerateProperties';
import type {
  EnhancerTargetFullOptions,
} from '@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/models';
import { EnhancerTargetOptionsItem } from '@/sdk/constants';

type Options = {
  integrateWithAlias?: boolean;
  autoMatchMultilevelString?: boolean;
  autoGenerateProperties?: boolean;
};

interface IProps {
  options?: Options;
  optionsItems?: EnhancerTargetOptionsItem[];
  onChange?: (options: Partial<EnhancerTargetFullOptions>) => void;
}

export default ({ options: propsOptions, optionsItems, onChange }: IProps) => {
  const [options, setOptions] = useState<Options>(propsOptions ?? {});


  useEffect(() => {
    setOptions(propsOptions ?? {});
  }, [propsOptions]);

  const patchOptions = (patches: Options, triggerChange: boolean = true) => {
    const no = {
      ...options,
      ...patches,
    };
    setOptions(no);
    if (triggerChange) {
      onChange?.(no);
    }
  };

  const finalOptions = propsOptions ?? options;

  return (
    <>
      {optionsItems?.map((item, index) => {
        switch (item) {
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
