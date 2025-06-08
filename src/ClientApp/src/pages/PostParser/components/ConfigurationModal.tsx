import { useTranslation } from 'react-i18next';
import type { TextareaProps } from '@/components/bakaui';
import { Modal, NumberInput, Tab, Tabs, Textarea } from '@/components/bakaui';
import store from '@/store';
import BApi from '@/sdk/BApi';
import type { DestroyableProps } from '@/components/bakaui/types';
import { EditableValue } from '@/components/EditableValue';

type Props = DestroyableProps;

export default (props: Props) => {
  const { t } = useTranslation();
  const soulPlusOptions = store.useModelState('soulPlusOptions');
  return (
    <Modal
      defaultVisible
      size={'xl'}
      onDestroyed={props.onDestroyed}
      footer={{
        actions: ['cancel'],
      }}
    >
      <Tabs aria-label="Options" isVertical>
        <Tab key="soulplus" title={t('SoulPlus')}>
          <div className={'flex flex-col gap-2'}>
            <EditableValue
              Viewer={Textarea}
              Editor={Textarea}
              label={t('Cookie')}
              description={t('Cookie is used to access SoulPlus posts.')}
              value={soulPlusOptions.cookie}
              onSubmit={async v => {
                await BApi.options.patchSoulPlusOptions({
                  cookie: v,
                });
              }}
            />
            <EditableValue
              Viewer={NumberInput}
              Editor={NumberInput}
              editorProps={{
                min: 0,
                formatOptions: { useGrouping: false },
                hideStepper: true,
              }}
              viewerProps={{
                formatOptions: { useGrouping: false },
                hideStepper: true,
              }}
              label={t('Auto buy threshold')}
              description={t('Items priced below this value will be bought automatically.')}
              value={soulPlusOptions.autoBuyThreshold}
              onSubmit={async v => {
                await BApi.options.patchSoulPlusOptions({
                  autoBuyThreshold: v,
                });
              }}
            />
          </div>
        </Tab>
      </Tabs>
    </Modal>
  );
};
