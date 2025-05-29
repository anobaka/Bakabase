import { useTranslation } from 'react-i18next';
import { AiOutlineWarning } from 'react-icons/ai';
import Card from './Card';
import type { IdName } from '@/pages/SynchronizationOptions/models';
import { SubjectLabels } from '@/pages/SynchronizationOptions/models';
import type {
  BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationMediaLibraryOptions,
} from '@/sdk/Api';
import BooleanOptions from '@/pages/SynchronizationOptions/components/BooleanOptions';
import EnhancerOptions from '@/pages/SynchronizationOptions/components/EnhancerOptions';
import { Tooltip } from '@/components/bakaui';

type Options = BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationMediaLibraryOptions;

type MediaLibrary = IdName & { enhancers?: IdName[]};

type Props = {
  mediaLibrary: MediaLibrary;
  onChange?: (options: Options) => any;
  options?: Options;
};

export default ({
                  mediaLibrary,
                  onChange,
                  options,
                }: Props) => {
  const { t } = useTranslation();

  const patchOptions = (patches: Partial<Options>) => {
    const newOptions = {
      ...options,
      ...patches,
    };
    onChange?.(newOptions);
  };

  // console.log(options, mediaLibrariesOptionsMap);

  return (
    <Card header={mediaLibrary.name}>
      <BooleanOptions
        subject={t(SubjectLabels.DeleteResourcesWithUnknownPath)}
        onSelect={isSelected => patchOptions({ deleteResourcesWithUnknownPath: isSelected })}
        isSelected={options?.deleteResourcesWithUnknownPath}
      />
      <div />
      {mediaLibrary.enhancers?.map(e => {
        return (
          <>
            <EnhancerOptions
              options={options?.enhancerOptionsMap?.[e.id]}
              enhancer={e}
              onChange={o => patchOptions({
                enhancerOptionsMap: {
                  ...options?.enhancerOptionsMap,
                  [e.id]: o,
                },
              })}
            />
          </>
        );
      })}
    </Card>
  );
};
