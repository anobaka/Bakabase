import { useTranslation } from 'react-i18next';
import toast from 'react-hot-toast';
import type { PropertyType } from '@/sdk/constants';
import { Modal } from '@/components/bakaui';
import BlockSort from '@/components/BlockSort';
import BApi from '@/sdk/BApi';
import type { DestroyableProps } from '@/components/bakaui/types';

type PropertyLike = {
  id: number;
  name: string;
  type: PropertyType;
};

type Props = {
  categoryId: number;
  properties: PropertyLike[];
} & DestroyableProps;

export default ({ properties, onDestroyed, categoryId, onClose }: Props) => {
  const { t } = useTranslation();
  return (
    <Modal
      defaultVisible
      size={'xl'}
      footer={{
        actions: ['cancel'],
        cancelProps: {
          text: t('Close'),
        },
      }}
      title={t('Adjust orders of properties')}
      onDestroyed={onDestroyed}
      onClose={onClose}
    >
      <div>{t('You can adjust orders or properties by dragging and dropping them')}</div>
      <BlockSort
        blocks={properties}
        onSorted={async ids => {
          await BApi.category.sortCustomPropertiesInCategory(categoryId, { orderedPropertyIds: ids });
          toast.success(t('Saved'));
        }}
      />
    </Modal>
  );
};
