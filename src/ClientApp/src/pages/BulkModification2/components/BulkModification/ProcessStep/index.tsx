'use strict';
import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import { Chip } from '@/components/bakaui';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import type { IProperty } from '@/components/Property/models';
import type {
  BulkModificationProcessStep,
  BulkModificationVariable,
} from '@/pages/BulkModification2/components/BulkModification/models';
import ProcessStepModel from '@/pages/BulkModification2/components/BulkModification/ProcessStepModel';

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
      <Chip
        size={'sm'}
        radius={'sm'}
      >{no}</Chip>
      针对
      <Chip
        size={'sm'}
        radius={'sm'}
        variant={'light'}
        color={'primary'}
      >
        全部
      </Chip>
      数据，从
      <Chip
        size={'sm'}
        radius={'sm'}
        variant={'light'}
        color={'primary'}
      >
        全部
      </Chip>
      第
      <Chip
        size={'sm'}
        radius={'sm'}
        variant={'light'}
        color={'primary'}
      >
        全部
      </Chip>
      个字符
      <Chip
        size={'sm'}
        radius={'sm'}
        variant={'light'}
        color={'primary'}
      >
        向后
      </Chip>
      <Chip
        size={'sm'}
        radius={'sm'}
        variant={'light'}
        color={'primary'}
      >
        移除
      </Chip>
      <Chip
        size={'sm'}
        radius={'sm'}
        variant={'light'}
        color={'primary'}
      >
        5
      </Chip>
      个字符
    </div>
  );
};
