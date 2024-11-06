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
        {t('Please note that if you modify the path of the resource, the new path will be considered as a new resource, at meantime the old resource data will be deleted.')}
      </div>
    </Modal>
  );
};
