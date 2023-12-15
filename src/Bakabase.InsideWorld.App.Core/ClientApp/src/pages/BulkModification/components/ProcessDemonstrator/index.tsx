import { useTranslation } from 'react-i18next';
import { useCallback } from 'react';
import { useUpdateEffect } from 'react-use';
import DateTimeProcessor from '../ProcessDialog/Processors/DateTimeProcessor';
import EnumProcessor from '../ProcessDialog/Processors/EnumProcessor';
import TextProcessor from '../ProcessDialog/Processors/TextProcessor';
import MultiValueProcessor from '../ProcessDialog/Processors/MultiValueProcessor';
import type { IBulkModificationProcess } from '@/pages/BulkModification';
import { ProcessorType, PropertyProcessorTypeMap } from '@/pages/BulkModification/components/ProcessDialog/models';
import SimpleLabel from '@/components/SimpleLabel';
import CustomIcon from '@/components/CustomIcon';
import { BulkModificationProperty, resourceLanguages } from '@/sdk/constants';
import ProcessDialog from '@/pages/BulkModification/components/ProcessDialog';
import type { IVariable } from '@/pages/BulkModification/components/Variables';
import variables from '@/pages/BulkModification/components/Variables';

interface IProps {
  index: number;
  process: IBulkModificationProcess;
  variables?: IVariable[];
  onChange: (data: IBulkModificationProcess) => any;
  dataSources?: Record<any, any>;
}

export default ({
                  process,
                  index,
                  onChange,
                  variables,
                  dataSources,
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
        break;
      case ProcessorType.Language:
        return (
          <EnumProcessor.Demonstrator
            value={process.value}
            dataSource={resourceLanguages.map(l => ({ ...l, label: t(l.label) }))}
          />
        );
      case ProcessorType.Originals:
        break;
      case ProcessorType.Volume:
        break;
      case ProcessorType.Tag:
        break;
      case ProcessorType.Publisher:
        return (
          <MultiValueProcessor.Demonstrator
            getDataSource={getDataSource}
            value={process.value}
          />
        );
    }
    return (
      <div>
        {t('Unsupported processor type')}
      </div>
    );
  };

  return (
    <div
      className="process-demonstrator"
      onClick={() => {
        ProcessDialog.show({
          variables: variables,
          onSubmit: pv => {
            if (onChange) {
              onChange(pv);
            }
          },
        });
    }}
    >
      <div className="no">
        <SimpleLabel status={'default'}>{index + 1}</SimpleLabel>
      </div>
      <div className="property">
        <CustomIcon type={'segment'} size={'xs'} />
        {t(BulkModificationProperty[process.property!])}
      </div>
      {renderProcessorValue()}
    </div>
  );
};
