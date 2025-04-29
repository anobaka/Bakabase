import { useTranslation } from 'react-i18next';
import BooleanOptions from '@/pages/SynchronizationOptions/components/BooleanOptions';
import type {
  BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationEnhancerOptions,
} from '@/sdk/Api';
import { Chip } from '@/components/bakaui';
import { EnhancerIcon } from '@/components/Enhancer';
import { SubjectLabels } from '@/pages/SynchronizationOptions/models';

type Options = BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationEnhancerOptions;

type Props = {
  enhancer: { id: number; name: string };
  options?: Options;
  onChange?: (options: Options) => void;
  subject?: any;
  isSecondary?: boolean;
};

export default ({
                  enhancer,
                  options,
                  onChange,
                  subject,
                  isSecondary,
                }: Props) => {
  const { t } = useTranslation();
  const enhancerChip = (
    <Chip
      size={'sm'}
      radius={'sm'}
      variant={'flat'}
    >
      <div className={'flex items-center gap-1'}>
        {t('Enhancer')}
        <EnhancerIcon id={enhancer.id} />
        {t(enhancer.name)}
      </div>
    </Chip>
  );

  const patchOptions = (patches: Partial<Options>) => {
    const newOptions = {
      ...options,
      ...patches,
    };
    onChange?.(newOptions);
  };
  return (
    <>
      <BooleanOptions
        subject={subject ?? (
          <>
            {enhancerChip}
            {t(SubjectLabels.EnhancerReApply)}
          </>
        )}
        isSelected={options?.reApply}
        onSelect={isSelected => {
          patchOptions({ reApply: isSelected });
        }}
        isSecondary={isSecondary}
      />
      <BooleanOptions
        subject={subject ?? (
          <>
            {enhancerChip}
            {t(SubjectLabels.EnhancerReEnhance)}
          </>
        )}
        isSelected={options?.reEnhance}
        onSelect={isSelected => {
          patchOptions({ reEnhance: isSelected });
        }}
        isSecondary={isSecondary}
      />
    </>
  );
};
