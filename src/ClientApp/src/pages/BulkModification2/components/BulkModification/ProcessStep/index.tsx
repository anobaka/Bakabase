'use strict';
import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import { TextProcessDemonstrator } from '../Processes/TextProcess';
import { Chip } from '@/components/bakaui';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import type { IProperty } from '@/components/Property/models';
import type {
  BulkModificationProcessStep,
  BulkModificationVariable,
} from '@/pages/BulkModification2/components/BulkModification/models';
import ProcessStepModel from '@/pages/BulkModification2/components/BulkModification/ProcessStepModel';
import { PropertyType } from '@/sdk/constants';

type Props = {
  no: number;
  step: BulkModificationProcessStep;
  property: IProperty;
  onChange?: (step: BulkModificationProcessStep) => any;
  variables?: BulkModificationVariable[];
};

export default ({
                  no,
                  step: propsStep,
                  property,
                  onChange,
                  variables,
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
      className={'flex items-center flex-wrap gap-1 cursor-pointer'}
      onClick={() => {
        createPortal(
          ProcessStepModel, {
            property: property,
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
          },
        );
      }}
    >
      {renderDemonstrator()}
      {/* <Chip */}
      {/*   size={'sm'} */}
      {/*   radius={'sm'} */}
      {/* >{no}</Chip> */}
      {/* 针对 */}
      {/* <Chip */}
      {/*   size={'sm'} */}
      {/*   radius={'sm'} */}
      {/*   variant={'light'} */}
      {/*   color={'primary'} */}
      {/* > */}
      {/*   全部 */}
      {/* </Chip> */}
      {/* 数据，从 */}
      {/* <Chip */}
      {/*   size={'sm'} */}
      {/*   radius={'sm'} */}
      {/*   variant={'light'} */}
      {/*   color={'primary'} */}
      {/* > */}
      {/*   全部 */}
      {/* </Chip> */}
      {/* 第 */}
      {/* <Chip */}
      {/*   size={'sm'} */}
      {/*   radius={'sm'} */}
      {/*   variant={'light'} */}
      {/*   color={'primary'} */}
      {/* > */}
      {/*   全部 */}
      {/* </Chip> */}
      {/* 个字符 */}
      {/* <Chip */}
      {/*   size={'sm'} */}
      {/*   radius={'sm'} */}
      {/*   variant={'light'} */}
      {/*   color={'primary'} */}
      {/* > */}
      {/*   向后 */}
      {/* </Chip> */}
      {/* <Chip */}
      {/*   size={'sm'} */}
      {/*   radius={'sm'} */}
      {/*   variant={'light'} */}
      {/*   color={'primary'} */}
      {/* > */}
      {/*   移除 */}
      {/* </Chip> */}
      {/* <Chip */}
      {/*   size={'sm'} */}
      {/*   radius={'sm'} */}
      {/*   variant={'light'} */}
      {/*   color={'primary'} */}
      {/* > */}
      {/*   5 */}
      {/* </Chip> */}
      {/* 个字符 */}
    </div>
  );
};
