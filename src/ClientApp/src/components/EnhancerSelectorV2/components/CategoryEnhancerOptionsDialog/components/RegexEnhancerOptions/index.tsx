import React, { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import toast from 'react-hot-toast';
import { useUpdate, useUpdateEffect } from 'react-use';
import { buildLogger, findCapturingGroupsInRegex } from '@/components/utils';
import { Chip, Textarea } from '@/components/bakaui';
import store from '@/store';
import BApi from '@/sdk/BApi';
import type {
  EnhancerFullOptions,
} from '@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/models';
import { type PropertyPool, RegexEnhancerTarget } from '@/sdk/constants';
import DynamicTargets
  from '@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/components/DynamicTargets';
import type { EnhancerDescriptor } from '@/components/EnhancerSelectorV2/models';
import type { IProperty } from '@/components/Property/models';

const log = buildLogger('RegexEnhancerOptions');

type Props = {
  propertyMap?: { [key in PropertyPool]?: Record<number, IProperty> };
  options?: EnhancerFullOptions;
  category: { name: string; id: number; customPropertyIds?: number[] };
  enhancer: EnhancerDescriptor;
};

const extractCaptureGroups = (expressions: string[]) => expressions.reduce<string[]>((s, t) => {
  s.push(...findCapturingGroupsInRegex(t));
  return s;
}, []);

export default ({
                  options: propsOptions,
                  enhancer,
                  propertyMap,
                  category,
                }: Props) => {
  const { t } = useTranslation();
  const enhancerOptions = store.useModelState('enhancerOptions');
  const [tmpEnhancerOptions, setTmpEnhancerOptions] = useState(enhancerOptions || {});
  const tmpEnhancerOptionsRef = useRef(tmpEnhancerOptions);
  const [options, setOptions] = useState(propsOptions);
  const optionsRef = useRef(options);
  const forceUpdate = useUpdate();

  useUpdateEffect(() => {
    optionsRef.current = propsOptions;
    addCaptureGroupsToOptions();
  }, [propsOptions]);

  useUpdateEffect(() => {
    optionsRef.current = options;
  }, [options]);

  useEffect(() => {
    log('new enhancer options', enhancerOptions);
    setTmpEnhancerOptions(JSON.parse(JSON.stringify(enhancerOptions || {})));
  }, [enhancerOptions]);

  useUpdateEffect(() => {
    tmpEnhancerOptionsRef.current = tmpEnhancerOptions;
    addCaptureGroupsToOptions();
  }, [tmpEnhancerOptions]);

  const addCaptureGroupsToOptions = () => {
    const newOptions: EnhancerFullOptions = {};
    const expressions = tmpEnhancerOptionsRef.current?.regexEnhancer?.expressions || [];
    const captureGroups = extractCaptureGroups(expressions);
    log(captureGroups, optionsRef.current?.targetOptions);

    if (optionsRef.current?.targetOptions?.length) {
      newOptions.targetOptions = optionsRef.current.targetOptions.filter(x => x.dynamicTarget == undefined || captureGroups.includes(x.dynamicTarget));
    }

    for (const cg of captureGroups) {
      newOptions.targetOptions ??= [];
      if (!optionsRef.current?.targetOptions?.find(x => x.target == RegexEnhancerTarget.CaptureGroups && x.dynamicTarget == cg)) {
        newOptions.targetOptions.push({
          target: RegexEnhancerTarget.CaptureGroups,
          dynamicTarget: cg,
        });
      }
    }
    log(newOptions);
    setOptions({ ...newOptions });
  };

  const expressions = tmpEnhancerOptions?.regexEnhancer?.expressions || [];
  const captureGroups = extractCaptureGroups(expressions);
  return (
    <>
      <div>
        <Textarea
          minRows={3}
          maxRows={10}
          label={t('Regex expressions')}
          value={tmpEnhancerOptions?.regexEnhancer?.expressions?.join('\n')}
          onValueChange={v => {
            setTmpEnhancerOptions({
              ...tmpEnhancerOptions,
              regexEnhancer: {
                ...(tmpEnhancerOptions?.regexEnhancer || {}),
                expressions: v.split('\n'),
              },
            });
          }}
          onBlur={() => {
            BApi.options.patchEnhancerOptions(tmpEnhancerOptions).then(r => {
              if (!r.code) {
                toast.success(t('Successfully saved'));
              }
            });
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
            category={category}
            enhancer={enhancer}
            options={options}
            propertyMap={propertyMap}
          />
        </div>
      )}
    </>
  );
};
