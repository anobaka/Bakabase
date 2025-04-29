import { useTranslation } from 'react-i18next';
import Card from './Card';
import BooleanOptions from './BooleanOptions';
import type {
  BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationOptionsModel,
} from '@/sdk/Api';
import { SubjectLabels } from '@/pages/SynchronizationOptions/models';
import { enhancerIds } from '@/sdk/constants';
import EnhancerOptions from '@/pages/SynchronizationOptions/components/EnhancerOptions';

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
    <Card header={t('Global')}>
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
    </Card>
  );
};
