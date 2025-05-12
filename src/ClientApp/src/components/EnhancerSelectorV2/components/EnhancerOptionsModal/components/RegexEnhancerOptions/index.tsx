import React, { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import toast from 'react-hot-toast';
import { useUpdate, useUpdateEffect } from 'react-use';
import DynamicTargets from '../DynamicTargets';
import { buildLogger, findCapturingGroupsInRegex } from '@/components/utils';
import { Chip, Textarea } from '@/components/bakaui';
import store from '@/store';
import type {
  EnhancerFullOptions, RegexEnhancerFullOptions,
} from '@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/models';
import { type PropertyPool, RegexEnhancerTarget } from '@/sdk/constants';
import type { EnhancerDescriptor } from '@/components/EnhancerSelectorV2/models';
import type { IProperty } from '@/components/Property/models';

const log = buildLogger('RegexEnhancerOptions');

type Props = {
  propertyMap?: { [key in PropertyPool]?: Record<number, IProperty> };
  options?: RegexEnhancerFullOptions;
  enhancer: EnhancerDescriptor;
  onPropertyChanged?: () => any;
  onChange?: (options: RegexEnhancerFullOptions) => any;
};

const extractCaptureGroups = (expressions: string[]) => expressions.reduce<string[]>((s, t) => {
  s.push(...findCapturingGroupsInRegex(t));
  return s;
}, []);

export default ({
                  options: propsOptions,
                  enhancer,
                  propertyMap,
                  onPropertyChanged,
                  onChange,
                }: Props) => {
  const { t } = useTranslation();
  const [options, setOptions] = useState<Partial<RegexEnhancerFullOptions>>(propsOptions ?? {});
  const forceUpdate = useUpdate();

  const expressions = options?.expressions || [];
  const captureGroups = extractCaptureGroups(expressions);

  const patchOptions = (patches: Partial<RegexEnhancerFullOptions>) => {
    const newOptions: Partial<RegexEnhancerFullOptions> = {
      ...options,
      ...patches,
    };
    setOptions(newOptions);

    console.log(newOptions);

    if (!newOptions.expressions || newOptions.expressions.length === 0) {
      return;
    }
    onChange?.(newOptions as RegexEnhancerFullOptions);
  };

  return (
    <>
      <div>
        <Textarea
          minRows={3}
          maxRows={10}
          label={t('Regex expressions')}
          value={options?.expressions?.join('\n')}
          onValueChange={v => {
            patchOptions({ expressions: v.split('\n') });
          }}
          description={(
            <div>
              {captureGroups.length > 0 ? (
                <div>
                  {t('Available capture groups:')}
                  {captureGroups.map(g => {
                    return (
                      <Chip
                        variant={'light'}
                        size={'sm'}
                      >{g}</Chip>
                    );
                  })}
                </div>
              ) : (
                <div>{t('No named capture groups were found, so the enhancement will not take effect.')}</div>
              )}
              <div>{t('You can set multiple regex expressions(separated by new line) to match the file or folder name of each resource.')}</div>
              <div>{t('Text matched by multiple capture groups with the same name will be merged into a list and deduplicated.')}</div>
              <div>{t('After setting regex expressions, you must go to category page to configure regex enhancer for each category.')}</div>
              <div>{t('You need to use the same name(index-based group name will be ignored) as the capture group for the dynamic enhancement target, otherwise the resource may not be enhanced.')}</div>
            </div>
          )}
        />
      </div>
      {options && (
        <div className={'flex flex-col gap-y-4'}>
          <DynamicTargets
            enhancer={enhancer}
            optionsList={options.targetOptions}
            propertyMap={propertyMap}
            onPropertyChanged={onPropertyChanged}
            onChange={ol => patchOptions({ targetOptions: ol })}
          />
        </div>
      )}
    </>
  );
};
