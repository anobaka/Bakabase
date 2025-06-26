import { useTranslation } from 'react-i18next';
import OptionsCard from './OptionsCard';
import BooleanOptions from './BooleanOptions';
import type {
  BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationOptionsModel,
} from '@/sdk/Api';
import { SubjectLabels } from '@/pages/SynchronizationOptions/models';
import { enhancerIds } from '@/sdk/constants';
import EnhancerOptions from '@/pages/SynchronizationOptions/components/EnhancerOptions';
import { Checkbox, NumberInput } from '@/components/bakaui';

type Options = BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationOptionsModel;

type Props = {
  options?: Options;
  onChange?: (options: Options) => any;
};

export default ({
                  options,
                  onChange,
                }: Props) => {
  const { t } = useTranslation();

  const patchOptions = (patches: Partial<Options>) => {
    const newOptions = {
      ...options,
      ...patches,
    };
    onChange?.(newOptions);
  };

  return (
    <OptionsCard header={t('Global')}>
      <div className={'flex justify-end'}>
        <NumberInput
          className={'w-[360px]'}
          label={t('Max threads')}
          size={'sm'}
          minValue={1}
          onValueChange={maxThreads => {
            patchOptions({ maxThreads });
          }}
          value={options?.maxThreads}
          description={t('To reduce synchronization time, we will, by default, use 40% of your CPU\'s maximum thread count (rounded down) for syncing the media library')}
          isClearable
          onClear={() => {
            patchOptions({ maxThreads: undefined });
          }}
        />
      </div>
      <div />
      <BooleanOptions
        subject={t(SubjectLabels.DeleteResourcesWithUnknownMediaLibrary)}
        onSelect={isSelected => patchOptions({ deleteResourcesWithUnknownMediaLibrary: isSelected })}
        isSelected={options?.deleteResourcesWithUnknownMediaLibrary}
      />
      <BooleanOptions
        subject={t(SubjectLabels.DeleteResourcesWithUnknownPath)}
        onSelect={isSelected => patchOptions({ deleteResourcesWithUnknownPath: isSelected })}
        isSelected={options?.deleteResourcesWithUnknownPath}
      />

      {enhancerIds.map(e => {
          return (
            <EnhancerOptions
              options={options?.enhancerOptionsMap?.[e.value]}
              onChange={o => patchOptions({
                enhancerOptionsMap: {
                  ...options?.enhancerOptionsMap,
                  [e.value]: o,
                },
              })}
              enhancer={{
                id: e.value,
                name: e.label,
              }}
            />
          );
        })}
    </OptionsCard>
  );
};
