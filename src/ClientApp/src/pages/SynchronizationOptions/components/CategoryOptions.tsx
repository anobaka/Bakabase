import { useTranslation } from 'react-i18next';
import { AiOutlineWarning } from 'react-icons/ai';
import OptionsCard from './OptionsCard';
import type { IdName } from '@/pages/SynchronizationOptions/models';
import { SubjectLabels } from '@/pages/SynchronizationOptions/models';
import type {
  BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationCategoryOptions,
} from '@/sdk/Api';
import BooleanOptions from '@/pages/SynchronizationOptions/components/BooleanOptions';
import EnhancerOptions from '@/pages/SynchronizationOptions/components/EnhancerOptions';
import { Tooltip } from '@/components/bakaui';

type Options = BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationCategoryOptions;

type Category = { name: string; id: number; enhancers?: IdName[]; mediaLibraries?: IdName[] };

type Props = {
  category: Category;
  onChange?: (options: Options) => any;
  options?: Options;
};

/**
 * @deprecated
 */
const CategoryOptions = ({
                           category,
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
    <OptionsCard header={(
      <div className={'flex items-center gap-1 line-through opacity-60'}>
        {category.name}
        <Tooltip content={t('Category is deprecated and will be removed in a future version.')}>
          <div className={'flex items-center gap-1'}>
            <AiOutlineWarning className={'text-lg'} />
            {t('Deprecated')}
          </div>
        </Tooltip>
      </div>
    )}
    >
      <BooleanOptions
        subject={t(SubjectLabels.DeleteResourcesWithUnknownPath)}
        onSelect={isSelected => patchOptions({ deleteResourcesWithUnknownPath: isSelected })}
        isSelected={options?.deleteResourcesWithUnknownPath}
      />
      <div />
      {category.mediaLibraries?.map(l => {
        return (
          <BooleanOptions
            isSecondary
            subject={l.name}
            onSelect={isSelected => patchOptions({
              mediaLibraryOptionsMap: {
                ...options?.mediaLibraryOptionsMap,
                [l.id]: {
                  ...options?.mediaLibraryOptionsMap?.[l.id],
                  deleteResourcesWithUnknownPath: isSelected,
                },
              },
            })}
            isSelected={options?.mediaLibraryOptionsMap?.[l.id]?.deleteResourcesWithUnknownPath}
          />
        );
      })}
      <div />
      {category.enhancers?.map(e => {
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
            {category.mediaLibraries?.map(l => {
              return (
                <EnhancerOptions
                  isSecondary
                  options={options?.mediaLibraryOptionsMap?.[l.id]?.enhancerOptionsMap?.[e.id]}
                  enhancer={e}
                  onChange={o => patchOptions({
                    mediaLibraryOptionsMap: {
                      ...options?.mediaLibraryOptionsMap,
                      [l.id]: {
                        ...options?.mediaLibraryOptionsMap?.[l.id],
                        enhancerOptionsMap: {
                          ...options?.mediaLibraryOptionsMap?.[l.id]?.enhancerOptionsMap,
                          [e.id]: o,
                        },
                      },
                    },
                  })}
                  subject={l.name}
                />
              );
            })}
          </>
        );
      })}
    </OptionsCard>
  );
};

export default CategoryOptions;
