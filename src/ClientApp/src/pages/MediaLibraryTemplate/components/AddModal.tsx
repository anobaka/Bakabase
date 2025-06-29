import { Trans, useTranslation } from 'react-i18next';
import { useState } from 'react';
import type { DestroyableProps } from '@/components/bakaui/types';
import { Button, Divider, Input, Modal } from '@/components/bakaui';
import BuiltinTemplateSelector from '@/pages/MediaLibraryTemplate/components/PresetTemplateBuilder';
import type { components } from '@/sdk/BApi2';
import BApi from '@/sdk/BApi';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';


type Props = {} & DestroyableProps;

export default ({ onDestroyed }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [name, setName] = useState<string>();
  const [visible, setVisible] = useState<boolean>(true);

  return (
    <Modal
      size={'md'}
      visible={visible}
      title={t('Add a media library template')}
      defaultVisible
      onDestroyed={onDestroyed}
      footer={{
        actions: ['cancel', 'ok'],
        okProps: {
          isDisabled: name == undefined || name.length == 0,
        },
      }}
      onOk={async () => {
        const r = await BApi.mediaLibraryTemplate
          .addMediaLibraryTemplate({ name: name! });
        if (r.code) {
          throw new Error(r.message);
        }
        setVisible(false);
      }}
    >
      <div className={'flex flex-col gap-1'}>
        <div>
          <Input
            label={t('Template name')}
            placeholder={t('Enter template name')}
            isRequired
            onValueChange={setName}
            value={name}
          />
        </div>
        <div className={''}>
          <Trans i18nKey={'media-library-template.use-preset-template-builder'}>
            We recommend using our
            <Button
              size={'sm'}
              variant={'light'}
              onPress={() => {
                setVisible(false);
                createPortal(
                  BuiltinTemplateSelector, {
                    onSubmitted: onDestroyed,
                  },
                );
              }}
              color={'primary'}
            >
              {t('Preset template builder')}
            </Button>
            the first time you create a media library template.
          </Trans>
        </div>
      </div>
    </Modal>
  );
};
