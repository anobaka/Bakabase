'use strict';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import TextProcessor from '../Processors/TextProcessor';
import { Modal } from '@/components/bakaui';
import type { IProperty } from '@/components/Property/models';
import type { BulkModificationProcessorValueType } from '@/sdk/constants';
import { PropertyType } from '@/sdk/constants';
import type { DestroyableProps } from '@/components/bakaui/types';
import type { BulkModificationVariable } from '@/pages/BulkModification2/components/BulkModification/models';

type Props = {
  property: IProperty;
  operation?: number;
  options?: any;
  onSubmit?: (operation: number, options: any) => any;
  variables?: BulkModificationVariable[];
  availableValueTypes?: BulkModificationProcessorValueType[];
} & DestroyableProps;

export default ({ property, operation: propsOperation, options: propsOptions, onDestroyed, onSubmit, variables, availableValueTypes }: Props) => {
  const { t } = useTranslation();

  const [operation, setOperation] = useState<number | undefined>(propsOperation);
  const [options, setOptions] = useState<any>(propsOptions);

  const renderOptions = () => {
    switch (property.type) {
      case PropertyType.SingleLineText:
      case PropertyType.MultilineText:
      case PropertyType.Formula:
      case PropertyType.SingleChoice:
        return (
          <TextProcessor.Options
            options={options}
            operation={operation}
            useTextarea={(property.type == PropertyType.MultilineText || property.type == PropertyType.Formula)}
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
      onOk={() => {
        if (operation != undefined) {
          onSubmit?.(operation!, options);
        }
      }}
    >
      {renderOptions()}
    </Modal>
  );
};
