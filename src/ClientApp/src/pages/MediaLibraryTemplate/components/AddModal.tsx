import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import type { DestroyableProps } from '@/components/bakaui/types';
import { Button, Input, Modal } from '@/components/bakaui';
import BuiltinTemplateSelector from '@/pages/MediaLibraryTemplate/components/BuiltinTemplateSelector';
import type { components } from '@/sdk/BApi2';
import BApi from '@/sdk/BApi';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';


type BuiltinTemplate = components['schemas']['Bakabase.InsideWorld.Business.Components.BuiltinMediaLibraryTemplate.BuiltinMediaLibraryTemplateDescriptor'];
type Props = {} & DestroyableProps;

export default ({ onDestroyed }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [name, setName] = useState<string>();
  const [builtinTemplate, setBuiltinTemplate] = useState<BuiltinTemplate>();

  return (
    <Modal
      size={'md'}
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
          .addMediaLibraryTemplate({ name: name!, builtinTemplateId: builtinTemplate?.id });
        if (r.code) {
          throw new Error(r.message);
        }
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
        <div className={'flex items-center gap-1'}>
          <Button
            variant={'light'}
            onPress={() => {
              createPortal(
                BuiltinTemplateSelector, {
                  onSelect: template => {
                    setBuiltinTemplate(template);
                    if (name == undefined || name.length == 0) {
                      setName(template.name);
                    }
                  },
                },
              );
            }}
            color={builtinTemplate ? 'success' : 'primary'}
          >
            {builtinTemplate ? (
              <div>
                {t('Use base template')}&nbsp;{builtinTemplate.name}
              </div>
            ) : `${t('Select a base template')}(${t('Optional')})`}
          </Button>
        </div>
      </div>
    </Modal>
  );
};
