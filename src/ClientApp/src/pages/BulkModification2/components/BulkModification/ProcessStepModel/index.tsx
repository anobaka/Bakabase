'use strict';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { StringValueProcessEditor } from '../Processes/StringValueProcess';
import { ListStringValueProcessEditor } from '../Processes/ListStringValueProcess';
import { Chip, Modal } from '@/components/bakaui';
import type { IProperty } from '@/components/Property/models';
import { BulkModificationProcessorValueType } from '@/sdk/constants';
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
                  availableValueTypes = [BulkModificationProcessorValueType.Static],
                }: Props) => {
  const { t } = useTranslation();

  const [operation, setOperation] = useState<number | undefined>(propsOperation);
  const [options, setOptions] = useState<any>(propsOptions);
  const [error, setError] = useState<string | undefined>();

  log('property', property, 'operation', operation, 'options', options);

  const renderOptions = () => {
    switch (property.type) {
      case PropertyType.SingleLineText:
      case PropertyType.MultilineText:
      case PropertyType.Formula:
      case PropertyType.SingleChoice:
        return (
          <StringValueProcessEditor
            options={options}
            operation={operation}
            propertyType={property.type}
            onChange={(operation, options, error) => {
              setOperation(operation);
              setOptions(options);
              setError(error);
            }}
            variables={variables}
            availableValueTypes={availableValueTypes}
          />
        );
      case PropertyType.MultipleChoice:
      case PropertyType.Attachment:
        return (
          <ListStringValueProcessEditor
            options={options}
            operation={operation}
            property={property}
            onChange={(operation, options, error) => {
              setOperation(operation);
              setOptions(options);
              setError(error);
            }}
            variables={variables}
            availableValueTypes={availableValueTypes}
          />
        );
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
          isDisabled: !!error,
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
      {error && (
        <div className={'whitespace-break-spaces text-danger'} >
          {t('ERROR')}: {error}
        </div>
      )}
    </Modal>
  );
};
