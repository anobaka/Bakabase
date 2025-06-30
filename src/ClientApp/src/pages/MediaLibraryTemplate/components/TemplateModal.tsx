import { useTranslation } from 'react-i18next';
import { useUpdate } from 'react-use';
import { useEffect, useState } from 'react';
import type { DestroyableProps } from '@/components/bakaui/types';
import { Chip, Modal } from '@/components/bakaui';
import Template from '@/pages/MediaLibraryTemplate/components/Template';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import type { MediaLibraryTemplate } from '@/pages/MediaLibraryTemplate/models';
import BApi from '@/sdk/BApi';

type Props = {
  id: number;
} & DestroyableProps;

export default ({
                  id,
                  onDestroyed,
                }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const forceUpdate = useUpdate();

  const [template, setTemplate] = useState<MediaLibraryTemplate>();

  useEffect(() => {
    BApi.mediaLibraryTemplate.getMediaLibraryTemplate(id).then(r => {
      if (!r.code) {
        setTemplate(r.data!);
      }
    });
  }, []);

  return (
    <Modal
      onDestroyed={onDestroyed}
      size={'full'}
      title={(
        <div>
          {t('Editing media library template')}
          &nbsp;
          <Chip
            size={'lg'}
            color={'success'}
            variant={'light'}
            className={'font-bold'}
          >{template?.name}</Chip>
        </div>
      )}
      footer={false}
      defaultVisible
    >
      {(template) && (
        <Template
          template={template}
        />
      )}
    </Modal>
  );
};
