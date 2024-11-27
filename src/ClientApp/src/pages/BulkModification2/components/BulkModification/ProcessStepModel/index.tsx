'use strict';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { TextProcessEditor } from '../Processes/TextProcess';
import { Modal } from '@/components/bakaui';
import type { IProperty } from '@/components/Property/models';
import type { BulkModificationProcessorValueType } from '@/sdk/constants';
import { PropertyType } from '@/sdk/constants';
import type { DestroyableProps } from '@/components/bakaui/types';
import type { BulkModificationVariable } from '@/pages/BulkModification2/components/BulkModification/models';
import { buildLogger } from '@/components/utils';

type Props = {
  property: IProperty;
  operation?: number;
  options?: any;
  onSubmit?: (operation: number, options: any) => any;
  variables?: BulkModificationVariable[];
  availableValueTypes?: BulkModificationProcessorValueType[];
} & DestroyableProps;

const log = buildLogger('ProcessStepModel');

export default ({
                  property,
                  operation: propsOperation,
                  options: propsOptions,
                  onDestroyed,
                  onSubmit,
                  variables,
                  availableValueTypes,
                }: Props) => {
  const { t } = useTranslation();

  const [operation, setOperation] = useState<number | undefined>(propsOperation);
  const [options, setOptions] = useState<any>(propsOptions);

  log('property', property, 'operation', operation, 'options', options);

  const renderOptions = () => {
    switch (property.type) {
      case PropertyType.SingleLineText:
      case PropertyType.MultilineText:
      case PropertyType.Formula:
      case PropertyType.SingleChoice:
        return (
          <TextProcessEditor
            options={options}
            operation={operation}
            property={property}
            onChange={(operation, options) => {
              setOperation(operation);
              setOptions(options);
            }}
            variables={variables}
            availableValueTypes={availableValueTypes}
          />
        );
      case PropertyType.MultipleChoice:
      case PropertyType.Attachment:
        break;
      case PropertyType.Number:
      case PropertyType.Percentage:
      case PropertyType.Rating:
        break;
      case PropertyType.Boolean:
        break;
      case PropertyType.Link:
        break;
      case PropertyType.Date:
      case PropertyType.DateTime:
      case PropertyType.Time:
        break;
      case PropertyType.Multilevel:
        break;
      case PropertyType.Tags:
        break;
    }
    return t('Not supported');
  };

  return (
    <Modal
      defaultVisible
      size={'xl'}
      onDestroyed={onDestroyed}
      footer={{
        actions: ['ok', 'cancel'],
        okProps: {
          isDisabled: operation == undefined,
        },
      }}
      onOk={() => {
        if (operation != undefined) {
          log('onSubmit', 'operation', operation, 'options', options);
          onSubmit?.(operation!, options);
        }
      }}
    >
      {renderOptions()}
    </Modal>
  );
};
