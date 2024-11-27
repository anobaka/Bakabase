import { useTranslation } from 'react-i18next';
import React, { useState } from 'react';
import TextProcessor from '../Processes/TextProcess';
import type { DestroyableProps } from '@/components/bakaui/types';
import { Button, Card, Modal, Select } from '@/components/bakaui';
import PropertySelector from '@/components/PropertySelector';
import type { BulkModificationProcessorValueType } from '@/sdk/constants';
import { PropertyPool } from '@/sdk/constants';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import MultilevelProcessor from '@/pages/BulkModification2/components/BulkModification/Processors/MultilevelProcessor';
import type { BulkModificationProcess } from '@/pages/BulkModification2/components/BulkModification/models';

type Props = {
  process?: Partial<BulkModificationProcess>;
  multipleValueTypes?: boolean;
  fixProperty?: boolean;
} & DestroyableProps;

export default ({ onDestroyed, process: propsProcess, multipleValueTypes, fixProperty }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [process, setProcess] = useState<Partial<BulkModificationProcess>>(propsProcess ?? {});

  return (
    <Modal
      defaultVisible
      onDestroyed={onDestroyed}
      size={'xl'}
    >
      <div className={'grid items-center'} style={{ gridTemplateColumns: 'auto 1fr' }}>
        <div>{t('Value to be processed')}</div>
        <div>
          <Button
            size="sm"
            color={'primary'}
            isDisabled={fixProperty}
            variant={'light'}
            onClick={() => {
              createPortal(
                PropertySelector, {
                  pool: PropertyPool.All,
                  multiple: false,
                  onSubmit: async (ps) => {
                    const p = ps[0];
                    setProcess({
                      ...process,
                      propertyPool: p.pool,
                      propertyId: p.id,
                      property: p,
                    });
                  },
                },
              );
            }}
          >
            {process?.property ? process.property.name : t('Select a property')}
          </Button>
        </div>
      </div>
      <div>
        <MultilevelProcessor />
      </div>
    </Modal>
  );
};
