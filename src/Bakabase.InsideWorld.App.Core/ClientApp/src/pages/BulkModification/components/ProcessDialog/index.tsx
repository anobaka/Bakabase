import React, { useCallback, useRef, useState } from 'react';
import { Dialog, Select, Tag } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import {
  BulkModificationFilterOperation,
  bulkModificationFilterOperations, BulkModificationProcessOperation, bulkModificationProcessOperations,
  bulkModificationProperties,
  BulkModificationProperty,
} from '@/sdk/constants';
import './index.scss';
import { useUpdate, useUpdateEffect } from 'react-use';
import BApi from '@/sdk/BApi';
import { createPortalOfComponent } from '@/components/utils';
import type { IBulkModificationProcess } from '@/pages/BulkModification';
import { availablePropertiesForProcessing } from '@/pages/BulkModification/components/ProcessDialog/models';

interface IProps {
  process?: IBulkModificationProcess;
  onSubmit?: (value: IBulkModificationProcess) => any;
}

const ProcessDialog = (props: IProps) => {
  const { t } = useTranslation();
  const forceUpdate = useUpdate();

  const {
    process: propsProcess,
    onSubmit,
  } = props;
  const [visible, setVisible] = useState(true);
  const [process, setProcess] = useState<IBulkModificationProcess>(propsProcess || {});
  const propertyKeyCandidatesRef = useRef<{ label: string; value: string }[]>();

  useUpdateEffect(() => {
    if (process.property == BulkModificationProperty.CustomProperty && !propertyKeyCandidatesRef.current) {
      propertyKeyCandidatesRef.current = [];
      // get all custom property keys

      BApi.resource.getAllCustomPropertyKeys().then(r => {
        const keys = r.data || [];
        propertyKeyCandidatesRef.current = keys.map(k => ({
          label: k,
          value: k,
        }));
        forceUpdate();
      });
    }
  }, [process?.property]);

  const renderCustomPropertyKeySelector = () => {
    if (process.property != BulkModificationProperty.CustomProperty) {
      return null;
    }
    return (
      <>
        <div className="label">
          {t('Property Key')}
        </div>
        <div className="value">
          <Select
            dataSource={propertyKeyCandidatesRef.current || []}
            value={process?.propertyKey}
            onChange={v => setProcess({
              ...process,
              propertyKey: v,
            })}
            useVirtual
            showSearch
          />
        </div>
      </>
    );
  };

  const close = useCallback(() => {
    setVisible(false);
  }, []);


  console.log(process);

  return (
    <Dialog
      visible={visible}
      v2
      width={'auto'}
      className={'bulk-modification-filter-dialog'}
      closeMode={['close', 'mask', 'esc']}
      onClose={close}
      onCancel={close}
      onOk={() => {
        close();
        onSubmit && onSubmit(process);
      }}
    >
      <div className="filter-form">
        <div className="label">
          {t('Property')}
        </div>
        <div className="value">
          <Tag.Group>
            {bulkModificationProperties.map(p => {
              const currChecked = process.property == p.value;
              const available = availablePropertiesForProcessing.includes(p.value);
              return (
                <Tag.Selectable
                  key={p.value}
                  onChange={checked => {
                    if (!currChecked && checked) {
                      setProcess({
                        ...process,
                        property: p.value,
                        operation: undefined,
                        find: undefined,
                        replace: undefined,
                      });
                    }
                  }}
                  checked={currChecked}
                  disabled={!available}
                >{t(p.label)}</Tag.Selectable>
              );
            })}
          </Tag.Group>
        </div>
        {renderCustomPropertyKeySelector()}
        <div className="label">
          {t('Operation')}
        </div>
        <div className="value">
          <Tag.Group>
            {bulkModificationProcessOperations.map(op => {
              const currChecked = process.operation == op.value;
              return (
                <Tag.Selectable
                  key={op.value}
                  onChange={checked => {
                    if (!currChecked && checked) {
                      setProcess({
                        ...process,
                        operation: op.value,
                        find: undefined,
                        replace: undefined,
                      });
                    }
                  }}
                  checked={currChecked}
                  // disabled={!availableOperations.includes(op.value)}
                >{t(`BulkModificationProcessOperation.${BulkModificationProcessOperation[op.value]}`)}</Tag.Selectable>
              );
            })}
          </Tag.Group>
        </div>
      </div>
    </Dialog>
  );
};

ProcessDialog.show = (props: IProps) => createPortalOfComponent(ProcessDialog, props);

export default ProcessDialog;
