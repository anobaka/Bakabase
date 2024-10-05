import { useEffect, useState } from 'react';
import AutoMatchMultilevelString from './AutoMatchMultilevelString';
import AutoBindProperty from './AutoBindProperty';
import CoverSelectOrderComp from './CoverSelectOrder';
import type {
  EnhancerTargetFullOptions,
} from '@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/models';
import type { CoverSelectOrder } from '@/sdk/constants';
import { EnhancerTargetOptionsItem } from '@/sdk/constants';

type Options = {
  autoMatchMultilevelString?: boolean;
  autoBindProperty?: boolean;
  coverSelectOrder?: CoverSelectOrder;
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
          case EnhancerTargetOptionsItem.AutoBindProperty:
            return (
              <AutoBindProperty
                key={item}
                autoBindProperty={finalOptions.autoBindProperty ?? false}
                onChange={o => patchOptions({ autoBindProperty: o })}
              />
            );
          case EnhancerTargetOptionsItem.CoverSelectOrder:
            return (
              <CoverSelectOrderComp
                key={item}
                coverSelectOrder={finalOptions.coverSelectOrder}
                onChange={o => patchOptions({ coverSelectOrder: o })}
              />
            );
        }
      })}
    </>
  );
};
