import { useTranslation } from 'react-i18next';
import { Chip } from '@/components/bakaui';
import type { BulkModificationProcess } from '@/pages/BulkModification2/components/BulkModification/models';
import ProcessStep from '@/pages/BulkModification2/components/BulkModification/ProcessStep';

type Props = {
  process: BulkModificationProcess;
  hideProperty?: boolean;
};

export default ({ process, hideProperty }: Props) => {
  const { t } = useTranslation();
  return (
    <div className={'flex gap-1'}>
      {!hideProperty && (
        <div className={'flex items-center gap-1'}>
          <Chip
            size={'sm'}
            radius={'sm'}
          >1</Chip>
          <Chip
            size={'sm'}
            radius={'sm'}
            variant={'light'}
            color={'secondary'}
          >{t('Internal property')}</Chip>
          <Chip
            size={'sm'}
            radius={'sm'}
            variant={'light'}
            color={'primary'}
          >
            分类
          </Chip>
        </div>
      )}
      <div className={'pl-2 flex flex-col gap-1'}>
        {process.steps?.map(step => {
          return (
            <ProcessStep step={step} />
          );
        })}
      </div>
    </div>
  );
};
