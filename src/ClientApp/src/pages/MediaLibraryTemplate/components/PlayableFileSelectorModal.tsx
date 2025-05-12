import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AiOutlineCheckCircle, AiOutlineQuestionCircle } from 'react-icons/ai';
import { Card, CardBody, CardHeader, Chip, Modal, Select, Textarea, Tooltip } from '@/components/bakaui';
import type { EnhancerDescriptor } from '@/components/EnhancerSelectorV2/models';
import BApi from '@/sdk/BApi';
import type { DestroyableProps } from '@/components/bakaui/types';
import type { IdName } from '@/components/types';

type Selection = {
  extensionsGroupIds?: number[];
  extensions?: string[];
};

type Props = {
  selection?: Selection;
  extensionsGroups?: IdName[];
  onSubmit?: (selection: Selection) => any;
} & DestroyableProps;

export default ({
                  selection: propSelection,
                  extensionsGroups,
                  onSubmit,
                }: Props) => {
  const { t } = useTranslation();

  const [selection, setSelection] = useState<Selection>(propSelection ?? {});

  useEffect(() => {

  }, []);

  return (
    <Modal
      defaultVisible
      size={'xl'}
      onOk={() => {
        onSubmit?.(selection);
      }}
    >
      <div className={'flex flex-col gap-2'}>
        <Select
          selectionMode={'multiple'}
          fullWidth={false}
          label={t('Limit file type groups')}
          placeholder={t('Select from predefined file type groups')}
          dataSource={extensionsGroups?.map(l => ({
            label: l.name,
            value: l.id.toString(),
          }))}
          selectedKeys={(selection?.extensionsGroupIds ?? []).map(x => x.toString())}
          onSelectionChange={keys => {
            setSelection({
              ...selection,
              extensionsGroupIds: Array.from(keys).map(k => parseInt(k as string, 10)),
            });
          }}
        />
        <Textarea
          fullWidth={false}
          label={t('Limit file extensions')}
          placeholder={t('custom extensions, split by space')}
          isMultiline
          description={t('File type groups and extensions will be merged during filtering')}
          value={(selection?.extensions ?? []).join(' ')}
          onValueChange={text => {
            setSelection({
              ...selection,
              extensions: text.split(' ').map(t => t.trim()).map(t => `.${t.replace(/^[\.]+/, '')}`),
            });
          }}
        />
      </div>
    </Modal>
  );
};
