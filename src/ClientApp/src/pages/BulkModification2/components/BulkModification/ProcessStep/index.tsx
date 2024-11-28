'use strict';
import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import { TextProcessDemonstrator } from '../Processes/TextValueProcess';
import { Chip } from '@/components/bakaui';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import type { IProperty } from '@/components/Property/models';
import type {
  BulkModificationProcessStep,
  BulkModificationVariable,
} from '@/pages/BulkModification2/components/BulkModification/models';
import ProcessStepModel from '@/pages/BulkModification2/components/BulkModification/ProcessStepModel';
import { type BulkModificationProcessorValueType, PropertyType } from '@/sdk/constants';

type Props = {
  no: string;
  step: BulkModificationProcessStep;
  property: IProperty;
  onChange?: (step: BulkModificationProcessStep) => any;
  variables?: BulkModificationVariable[];
  availableValueTypes?: BulkModificationProcessorValueType[];
  editable?: boolean;
};

export default ({
                  no,
                  step: propsStep,
                  property,
                  onChange,
                  variables,
                  editable,
                  availableValueTypes,
                }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [step, setStep] = useState<BulkModificationProcessStep>(propsStep);

  const renderDemonstrator = () => {
    switch (property.type) {
      case PropertyType.SingleLineText:
      case PropertyType.MultilineText:
      case PropertyType.Formula:
      case PropertyType.SingleChoice:
        return (
          <TextProcessDemonstrator
            property={property}
            variables={variables}
            operation={step.operation}
            options={step.options}
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
    <div
      className={`flex items-center flex-wrap gap-1 ${editable ? 'cursor-pointer hover:bg-[var(--bakaui-overlap-background)] rounded' : ''}`}
      onClick={editable ? () => {
        createPortal(
          ProcessStepModel, {
            property: property,
            availableValueTypes,
            onSubmit: (operation, options) => {
              const newStep = {
                ...step,
                operation,
                options,
              };
              setStep(newStep);
              onChange?.(newStep);
            },
            variables: variables,
            operation: step.operation,
            options: step.options,
          },
        );
      } : undefined}
    >
      <Chip
        size={'sm'}
        radius={'sm'}
      >{no}</Chip>
      {renderDemonstrator()}
    </div>
  );
};
