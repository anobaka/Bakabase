import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Card, CardBody, CardHeader, Modal, Select, Textarea, Tooltip } from '@/components/bakaui';
import BApi from '@/sdk/BApi';
import type { DestroyableProps } from '@/components/bakaui/types';
import type { IdName } from '@/components/types';
import ExtensionGroupSelect from '@/components/ExtensionGroupSelect';
import ExtensionsInput from '@/components/ExtensionsInput';

type Selection = {
  extensionGroupIds?: number[];
  extensions?: string[];
};

type Props = {
  selection?: Selection;
  onSubmit?: (selection: Selection) => any;
} & DestroyableProps;

export default ({
                  selection: propSelection,
                  onSubmit,
                }: Props) => {
  const { t } = useTranslation();

  const [selection, setSelection] = useState<Selection>(propSelection ?? {});

  return (
    <Modal
      defaultVisible
      size={'xl'}
      onOk={() => onSubmit?.(selection)}
    >
      <div className={'flex flex-col gap-2'}>
        <ExtensionGroupSelect
          value={selection.extensionGroupIds}
          onSelectionChange={(ids) => {
            setSelection({
              ...selection,
              extensionGroupIds: ids,
            });
          }}
        />
        <ExtensionsInput
          label={t('Limit file extensions')}
          onValueChange={(v) => {
            setSelection({
              ...selection,
              extensions: v,
            });
          }}
          defaultValue={selection.extensions}
        />
      </div>
    </Modal>
  );
};
