import { useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
import { FileSearchOutlined, InfoCircleOutlined } from '@ant-design/icons';
import SimplePropertySelector from '../SimplePropertySelector';
import type { CustomPropertyType } from '@/sdk/constants';
import { ResourceProperty, StandardValueConversionLoss } from '@/sdk/constants';
import { Button, Chip, Modal, Tab, Tabs } from '@/components/bakaui';
import BApi from '@/sdk/BApi';
import type { IProperty } from '@/components/Property/models';

export interface MigrationTarget {
  subTargets?: MigrationTarget[];
  property: ResourceProperty;
  propertyKey?: string;
  dataCount: number;
  data?: any;
  dataForDisplay?: string[];
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

  const [label, setLabel] = useState<string>();
  const [dataDialogVisible, setDataDialogVisible] = useState(false);
  const [confirmDialogVisible, setConfirmDialogVisible] = useState(false);
  const [lossDataDialogVisible, setLossDataDialogVisible] = useState(false);

  const [selectedProperty, setSelectedProperty] = useState<IProperty>();

  const lossData = target.targetCandidates?.find(d => d.type == (selectedProperty?.dbValueType as unknown as CustomPropertyType))?.lossData;
  const hasLossData = lossData && Object.keys(lossData).length > 0;

  useEffect(() => {
    if (target.property == ResourceProperty.Volume && target.propertyKey != undefined) {
      setLabel(t(`${ResourceProperty[target.property]}.${target.propertyKey}`));
    } else {
      setLabel(target.propertyKey ?? t(ResourceProperty[target.property!]));
    }
  }, []);

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
          <div>
            <div className={'italic mb-2'}>
              {t('Data not relative to resource will not be migrated.')}
            </div>
            <div className={'flex flex-wrap gap-1'}>
              {target.dataForDisplay?.map(d => (
                <Chip>{d}</Chip>
              ))}
            </div>
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
            <Tabs>
              {Object.keys(lossData).map(k => {
                const lossType = StandardValueConversionLoss[parseInt(k, 10)];
                return (
                  <Tab title={`${t(`StandardValueConversionLoss.${lossType}`)}(${lossData[k].length})`} key={k}>
                    <div className={'flex flex-wrap gap-1'}>
                      {lossData[k].map(d => (
                        <Chip>{d}</Chip>
                      ))}
                    </div>
                  </Tab>
                );
              })}
            </Tabs>
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
