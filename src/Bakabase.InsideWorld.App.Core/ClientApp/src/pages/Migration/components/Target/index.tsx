import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import { FileSearchOutlined, InfoCircleOutlined } from '@ant-design/icons';
import SimplePropertySelector from '../SimplePropertySelector';
import type { CustomPropertyType } from '@/sdk/constants';
import { ResourceProperty, StandardValueConversionLoss } from '@/sdk/constants';
import { Button, Chip, Modal } from '@/components/bakaui';
import BApi from '@/sdk/BApi';
import type { IProperty } from '@/components/Property/models';

export interface MigrationTarget {
  label?: string;
  subTargets?: MigrationTarget[];
  property?: ResourceProperty;
  propertyKey?: string;
  dataCount: number;
  data?: any;
  targetCandidates?: {
    type: CustomPropertyType;
    lossData?: Record<string, string[]>;
  }[];
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
  const [lossDataDialogVisible, setLossDataDialogVisible] = useState(false);

  const [selectedProperty, setSelectedProperty] = useState<IProperty>();

  const lossData = target.targetCandidates?.find(d => d.type == (selectedProperty?.type as unknown as CustomPropertyType))?.lossData;
  const hasLossData = lossData && Object.keys(lossData).length > 0;

  return (
    <div className={`flex ${isLeaf ? '' : 'flex-col'} gap-2 mt-1 mb-1`}>
      {isLeaf && (
        <Modal
          title={t('Data')}
          size={'xl'}
          footer={{
            actions: ['ok'],
          }}
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
      {
        hasLossData && (
          <Modal
            title={t('Some data will be lost')}
            size={'xl'}
            footer={{
              actions: ['ok'],
            }}
            visible={lossDataDialogVisible}
            onClose={() => setLossDataDialogVisible(false)}
          >
            <div className={'flex flex-col gap-1'}>
              {Object.keys(lossData).map(k => {
                const lossType = StandardValueConversionLoss[parseInt(k, 10)];
                return (
                  <div>
                    <div className={'font-bold'}>{t(`StandardValueConversionLoss.${lossType}`)}</div>
                    <div className={'flex flex-wrap gap-1'}>
                      {lossData[k].map(d => (
                        <Chip>{d}</Chip>
                      ))}
                    </div>
                  </div>
                );
              })}
            </div>
          </Modal>
        )
      }
      <div className={'flex items-center gap-2 text-medium'}>
        {label}
        {isLeaf && (
          <Button
            size={'sm'}
            variant={'light'}
            color={'primary'}
            className={'cursor-pointer'}
            onClick={() => {
              setDataDialogVisible(true);
            }}
          >
            <FileSearchOutlined className={'text-small'} />
            {target.dataCount}
          </Button>
        )}
      </div>
      {target.subTargets ? (
        <div className={'ml-4'}>
          {target.subTargets.map((subTarget, i) => {
            return (
              <div key={i} className={'flex items-center gap-2'}>
                <Target
                  target={subTarget}
                  isLeaf={!subTarget?.subTargets}
                  onMigrated={onMigrated}
                />
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
            {t('Are you sure to migrate [{{target}}] to [{{property}}]?', { target: label, property: selectedProperty?.name })}
          </Modal>
          {t('Convert to')}
          <SimplePropertySelector
            onSelected={p => setSelectedProperty(p)}
            valueTypes={target.targetCandidates?.map(tc => tc.type)}
          />
          {hasLossData && (
            <Button
              color={'danger'}
              size={'sm'}
              variant={'light'}
              onClick={() => {
                setLossDataDialogVisible(true);
              }}
            >
              <InfoCircleOutlined className={'text-small'} />
              {t('Some data will be lost')}, {t('check them here')}
            </Button>
          )}
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
