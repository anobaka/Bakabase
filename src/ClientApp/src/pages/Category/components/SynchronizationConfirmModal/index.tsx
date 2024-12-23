import { useTranslation } from 'react-i18next';
import { Modal } from '@/components/bakaui';
import type { DestroyableProps } from '@/components/bakaui/types';

type Props = {
  onOk: () => any;
} & DestroyableProps;

export default ({
                  onOk,
                  onDestroyed,
                }: Props) => {
  const { t } = useTranslation();
  return (
    <Modal
      size={'md'}
      defaultVisible
      onDestroyed={onDestroyed}
      title={t('Synchronize resource data')}
      onOk={onOk}
    >
      <div className={''}>
        {t('All eligible files or folders under the specified root directory will be saved to the media library.')}
      </div>
      <div className={'text-warning text-sm'}>
        {t('Please note, if you modify the resource path (generally the file name), a new resource associated with the new path will be created after synchronization. You can transfer historical resource data to the new resource or directly delete the historical resource through the resource page.')}
      </div>
    </Modal>
  );
};
