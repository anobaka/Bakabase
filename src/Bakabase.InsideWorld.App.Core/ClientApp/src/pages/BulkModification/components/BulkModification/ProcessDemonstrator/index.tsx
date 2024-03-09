import { useTranslation } from 'react-i18next';
import { useCallback } from 'react';
import { useUpdateEffect } from 'react-use';
import { ErrorBoundary } from 'react-error-boundary';
import DateTimeProcessor from '../ProcessDialog/Processors/DateTimeProcessor';
import EnumProcessor from '../ProcessDialog/Processors/EnumProcessor';
import TextProcessor from '../ProcessDialog/Processors/TextProcessor';
import MultiValueProcessor from '../ProcessDialog/Processors/MultiValueProcessor';
import VolumeProcessor from '../ProcessDialog/Processors/VolumeProcessor';
import NumberProcessor from '../ProcessDialog/Processors/NumberProcessor';
import type { IBulkModificationProcess } from '../../BulkModification';
import { ProcessorType, PropertyProcessorTypeMap } from '../ProcessDialog/models';
import ProcessDialog from '../ProcessDialog';
import type { IVariable } from '../Variables';
import SimpleLabel from '@/components/SimpleLabel';
import CustomIcon from '@/components/CustomIcon';
import { BulkModificationProperty, resourceLanguages } from '@/sdk/constants';
import ClickableIcon from '@/components/ClickableIcon';

interface IProps {
  index: number;
  process: IBulkModificationProcess;
  variables?: IVariable[];
  onChange: (data: IBulkModificationProcess) => any;
  dataSources?: Record<any, any>;
  onRemove?: () => any;
  editable: boolean;
}

const PropertyIconMap: { [key in BulkModificationProperty]?: string } = {
  [BulkModificationProperty.Name]: 'name',
  [BulkModificationProperty.Introduction]: 'introduction',
  [BulkModificationProperty.FileName]: 'a-filename',
  [BulkModificationProperty.CustomProperty]: 'customization',
  [BulkModificationProperty.Series]: '',
  [BulkModificationProperty.ReleaseDt]: 'date',
  [BulkModificationProperty.Rate]: 'star',
  [BulkModificationProperty.Language]: 'language',
  [BulkModificationProperty.Original]: '',
  [BulkModificationProperty.Volume]: '',
  [BulkModificationProperty.Tag]: 'tags',
  [BulkModificationProperty.Publisher]: 'users',
};

export default ({
                  process,
                  index,
                  onChange,
                  variables,
                  dataSources,
                  onRemove,
  editable,
                }: IProps) => {
  const { t } = useTranslation();
  const processorType = PropertyProcessorTypeMap[process.property!];

  // console.log('[ProcessDemonstrator] dataSources', dataSources);

  const getDataSource = useCallback((keys?: any[]): Promise<string[]> => {
    const result = keys?.map<string>(k => (dataSources?.[k] || k)?.toString()) || [];
    // console.log(12345, keys, result, dataSources);
    return Promise.resolve(result);
  }, [dataSources]);

  useUpdateEffect(() => {
    // console.log('[ProcessDemonstrator] dataSources changed', dataSources);
  }, [dataSources]);

  const renderProcessorValue = () => {
    switch (processorType) {
      case ProcessorType.Text:
        return (
          <TextProcessor.Demonstrator value={process.value} />
        );
      case ProcessorType.DateTime:
        return (
          <DateTimeProcessor.Demonstrator value={process.value} />
        );
      case ProcessorType.Number:
        return (
          <NumberProcessor.Demonstrator value={process.value} />
        );
      case ProcessorType.Language:
        return (
          <EnumProcessor.Demonstrator
            value={process.value}
            dataSource={resourceLanguages.map(l => ({
              ...l,
              label: t(l.label),
            }))}
          />
        );
      case ProcessorType.Originals:
        return (
          <MultiValueProcessor.Demonstrator
            getDataSource={getDataSource}
            value={process.value}
          />
        );
      case ProcessorType.Volume:
        return (
          <VolumeProcessor.Demonstrator
            value={process.value}
          />
        );
      case ProcessorType.Tag:
        console.log(process.value);
        return (
          <MultiValueProcessor.Demonstrator
            getDataSource={getDataSource}
            value={process.value}
          />
        );
      case ProcessorType.Publisher:
        return (
          <MultiValueProcessor.Demonstrator
            getDataSource={getDataSource}
            value={process.value}
          />
        );
      default:
        console.error(process);
        throw new Error('Unsupported processor type');
    }
  };

  return (
    <div
      className="process-demonstrator"
      onClick={() => {
        if (editable) {
          ProcessDialog.show({
            variables: variables,
            process,
            onSubmit: pv => {
              if (onChange) {
                onChange(pv);
              }
            },
          });
        }
      }}
    >
      <div className="no">
        <SimpleLabel status={'default'}>{index + 1}</SimpleLabel>
      </div>
      <div className="property">
        <CustomIcon type={PropertyIconMap[process.property!] || 'segment'} size={'xs'} />
        {t(BulkModificationProperty[process.property!])}
      </div>
      {process.propertyKey != undefined && (
        <div className={'property-key'}>
          {process.propertyKey}
        </div>
      )}
      <ErrorBoundary fallback={<span>{t('Unsupported processor type')}</span>}>
        {renderProcessorValue()}
      </ErrorBoundary>
      {editable && (
        <ClickableIcon
          type={'delete'}
          colorType={'danger'}
          size={'small'}
          onClick={e => {
            e.stopPropagation();
            onRemove?.();
          }}
        />
      )}
    </div>
  );
};
