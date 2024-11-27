import { useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
import React from 'react';
import ProcessDemonstrator from '../ProcessDemonstrator';
import { Button, Divider } from '@/components/bakaui';
import { InternalProperty, PropertyPool, PropertyValueScope, ReservedProperty } from '@/sdk/constants';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import ProcessModal from '@/pages/BulkModification2/components/BulkModification/ProcessModal';
import type { BulkModificationProcess } from '@/pages/BulkModification2/components/BulkModification/models';

const testProcesses: BulkModificationProcess[] = [
  {
    propertyPool: PropertyPool.Internal,
    propertyId: InternalProperty.Category,
  },
  {
    propertyPool: PropertyPool.Reserved,
    propertyId: ReservedProperty.Rating,
  },
  {
    propertyPool: PropertyPool.Custom,
    propertyId: 4,
  },
];

type Props = {
  processes?: BulkModificationProcess[];
  multipleValueType?: boolean;
  fixProperty?: boolean;
};

export default ({ processes: propsProcesses, fixProperty, multipleValueType }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [processes, setProcesses] = useState<BulkModificationProcess[]>(propsProcesses || testProcesses);

  useEffect(() => {
  }, []);

  return (
    <div className={'p-1'}>
      <div className={'flex flex-col gap-1'}>
        {processes.map((process, i) => {
          return (
            <React.Fragment key={i}>
              <ProcessDemonstrator
                process={process}
              />
              { i != processes.length - 1 && (
                <div className={'px-2'}>
                  <Divider
                    orientation={'horizontal'}
                  />
                </div>
              )}
            </React.Fragment>
          );
        })}
      </div>
      <div>
        <Button
          size={'sm'}
          color={'primary'}
          variant={'ghost'}
          onClick={() => {
            createPortal(ProcessModal, {
              fixProperty,
            });
          }}
        >
          {t('Add')}
        </Button>
      </div>
    </div>
  );
};
