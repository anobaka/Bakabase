import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import SimplePropertySelector from '../SimplePropertySelector';
import { ResourceProperty } from '@/sdk/constants';
import { Button, Chip, Modal } from '@/components/bakaui';
import type { ICustomProperty } from '@/pages/CustomProperty/models';
import BApi from '@/sdk/BApi';

export interface MigrationTarget {
  label?: string;
  subTargets?: MigrationTarget[];
  property?: ResourceProperty;
  propertyKey?: string;
  dataCount: number;
  data?: any;
}

interface IProps {
  target: MigrationTarget;
  isLeaf: boolean;
  onMigrated?: () => any;
}

const Target = ({
                  target,
                  isLeaf,
                  onMigrated,
                }: IProps) => {
  const { t } = useTranslation();

  const label = target.label ?? target.propertyKey ?? t(ResourceProperty[target.property!]);
  const [dataDialogVisible, setDataDialogVisible] = useState(false);
  const [confirmDialogVisible, setConfirmDialogVisible] = useState(false);

  const [selectedProperty, setSelectedProperty] = useState<ICustomProperty>();

  return (
    <div className={`flex ${isLeaf ? '' : 'flex-col mt-2'} gap-2`}>
      {isLeaf && (
        <Modal
          size={'xl'}
          footer={false}
          visible={dataDialogVisible}
          onClose={() => setDataDialogVisible(false)}
        >
          <div className={'flex flex-wrap gap-1'}>
            {target.data.map(d => (
              <Chip>{d}</Chip>
            ))}
          </div>
        </Modal>
      )}
      <div className={'flex items-center gap-2 text-medium'}>
        {label}
        {isLeaf && (
          <Chip
            size={'sm'}
            className={'cursor-pointer'}
            onClick={() => {
              setDataDialogVisible(true);
            }}
          >
            {target.dataCount}
          </Chip>
        )}
      </div>
      {target.subTargets ? (
        <div className={'ml-4'}>
          {target.subTargets.map((subTarget, i) => {
            return (
              <div key={i} className={'flex items-center gap-2'}>
                <Target target={subTarget} isLeaf={!subTarget?.subTargets} />
              </div>
            );
          })}
        </div>
      ) : (
        <div className={'flex gap-2 items-center ml-4'}>
          <Modal
            visible={confirmDialogVisible}
            onClose={() => setConfirmDialogVisible(false)}
            onOk={async () => {
              await BApi.migration.migrateTarget({
                property: target.property,
                propertyKey: target.propertyKey,
                targetPropertyId: selectedProperty?.id,
              });
              onMigrated?.();
            }}
          >
            {t('Are you sure to migrate')} {label} {t('to')} {selectedProperty?.name}?
          </Modal>
          {t('Convert to')}
          <SimplePropertySelector onSelected={p => setSelectedProperty(p)} />
          {selectedProperty && (
            <Button
              color={'primary'}
              size={'sm'}
              onClick={() => {
                setConfirmDialogVisible(true);
              }}
            >
              {t('Migrate')}
            </Button>
          )}
        </div>
      )}
    </div>
  );
};

export default Target;
