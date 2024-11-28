import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import { Select } from '@/components/bakaui';
import { MultilevelValueRenderer } from '@/components/StandardValue';

enum MultilevelProcessorOperation {
  Set = 1,
  AddBranches = 2,
  ModifyNode = 3,
  AddLayer = 4,
}

const multilevelProcessorOperations = Object.keys(MultilevelProcessorOperation).map(x => ({
  label: x,
  value: MultilevelProcessorOperation[x],
}));

type MultilevelProcessorOptions = {
  operation?: MultilevelProcessorOperation;

};

type Props = {

};

export default ({}: Props) => {
  const { t } = useTranslation();
  const [options, setOptions] = useState<MultilevelProcessorOptions>({});

  const renderSubOptions = (options: MultilevelProcessorOptions) => {
    if (!options.operation) {
      return null;
    }

    switch (options.operation) {
      case MultilevelProcessorOperation.Set:
      case MultilevelProcessorOperation.AddBranches:
        return (
          <>
            <div>
              {t('Value')}
            </div>
            <MultilevelValueRenderer editor={{ }} />
          </>
        );
      case MultilevelProcessorOperation.ModifyNode:
        return (
          <>
            <div>{t('Filter branches by')}</div>
          </>
        );
      case MultilevelProcessorOperation.AddLayer:
        return (
          <>
          </>
        );
    }
  };

  return (
    <div className={'grid items-center gap-2'} style={{ gridTemplateColumns: 'auto minmax(0, 1fr)' }}>
      <div>
        {t('Operation')}
      </div>
      <Select
        dataSource={multilevelProcessorOperations.map(tpo => ({
          label: tpo.label,
          value: tpo.value,
        }))}
        selectionMode={'single'}
        onSelectionChange={keys => {
          setOptions({
            ...options,
            operation: parseInt(Array.from(keys || [])[0] as string, 10) as MultilevelProcessorOperation,
          });
        }}
      />
      {renderSubOptions(options)}
    </div>
  );
};
