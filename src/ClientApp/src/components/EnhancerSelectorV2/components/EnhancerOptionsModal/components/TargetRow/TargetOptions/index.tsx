import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import CoverSelectOrderComp from './CoverSelectOrder';
import type {
  EnhancerTargetFullOptions,
} from '@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/models';
import type { CoverSelectOrder } from '@/sdk/constants';
import { EnhancerTargetOptionsItem } from '@/sdk/constants';
import { Checkbox } from '@/components/bakaui';

type Options = {
  autoMatchMultilevelString?: boolean;
  autoBindProperty?: boolean;
  coverSelectOrder?: CoverSelectOrder;
};

type Props = {
  options?: Options;
  optionsItems?: EnhancerTargetOptionsItem[];
  onChange?: (options: Partial<EnhancerTargetFullOptions>) => void;
  isDisabled?: boolean;
};

export default ({ options: propsOptions, optionsItems, onChange, isDisabled }: Props) => {
  const { t } = useTranslation();
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
          // case EnhancerTargetOptionsItem.AutoMatchMultilevelString:
          //   return (
          //     <Checkbox
          //       size={'sm'}
          //       isSelected={finalOptions.autoMatchMultilevelString ?? false}
          //       onValueChange={o => patchOptions({ autoMatchMultilevelString: o })}
          //     >
          //       {t('Auto match on empty values')}
          //     </Checkbox>
          //   );
          // case EnhancerTargetOptionsItem.AutoBindProperty:
          //   return (
          //     <Checkbox
          //       size={'sm'}
          //       isSelected={finalOptions.autoBindProperty ?? false}
          //       onValueChange={o => patchOptions({ autoBindProperty: o })}
          //     >
          //       {t('Auto bind property')}
          //     </Checkbox>
          //   );
          case EnhancerTargetOptionsItem.CoverSelectOrder:
            return (
              <CoverSelectOrderComp
                isDisabled={isDisabled}
                key={item}
                coverSelectOrder={finalOptions.coverSelectOrder}
                onChange={o => patchOptions({ coverSelectOrder: o })}
              />
            );
          default:
            return null;
        }
      })}
    </>
  );
};
