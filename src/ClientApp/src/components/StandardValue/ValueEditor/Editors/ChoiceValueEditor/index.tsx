import { useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
import { EditOutlined, SearchOutlined } from '@ant-design/icons';
import type { ValueEditorProps } from '../../models';
import { Button, Input, Modal } from '@/components/bakaui';
import type { DestroyableProps } from '@/components/bakaui/types';
import { buildLogger } from '@/components/utils';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

type Data = { value: string; label: string };

type ChoiceValueEditorProps = ValueEditorProps<string[] | undefined> & DestroyableProps & {
  multiple: boolean;
  getDataSource: () => Promise<Data[] | undefined>;
};

const log = buildLogger('ChoiceValueEditor');

export default (props: ChoiceValueEditorProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const {
    multiple,
    getDataSource,
    value: propsValue,
    onValueChange,
  } = props;

  const [dataSource, setDataSource] = useState<Data[]>([]);
  const [keyword, setKeyword] = useState('');
  const [value, setValue] = useState<string[]>(propsValue ?? []);

  log(props);

  useEffect(() => {
    loadData();
  }, []);

  const loadData = async () => {
    const data = await getDataSource() ?? [];
    if (data.length > 0) {
      setDataSource(data);
    }
  };

  return (
    <Modal
      defaultVisible
      size={dataSource.length > 10 ? 'xl' : 'lg'}
      title={t('Select data')}
      onOk={async () => {
        const validValues = value.filter(v => dataSource.some(d => d.value == v));
        onValueChange?.(validValues, validValues.map(v => dataSource.find(x => x.value == v)?.label).filter(x => x) as string[]);
      }}
    >
      <div>
        <Input
          size={'sm'}
          value={keyword}
          startContent={<SearchOutlined className={'text-small'} />}
          onValueChange={v => {
            setKeyword(v);
          }}
        />
      </div>
      <div className={'flex flex-wrap gap-1'}>
        {dataSource.filter(d =>
          keyword.length == 0 || d.label.toLowerCase().includes(keyword.toLowerCase())).map(d => {
          return (
            <Button
              size={'sm'}
              color={value.includes(d.value) ? 'primary' : 'default'}
              onClick={() => {
                if (multiple) {
                  log('value', value, 'select', d.value, 'includes', value.includes(d.value));
                  if (value.includes(d.value)) {
                    setValue(value.filter(v => v !== d.value));
                  } else {
                    setValue([...(value || []), d.value]);
                  }
                } else {
                  setValue([d.value]);
                }
              }}
            >
              {d.label}
            </Button>
          );
        })}
      </div>
    </Modal>
  );
};
