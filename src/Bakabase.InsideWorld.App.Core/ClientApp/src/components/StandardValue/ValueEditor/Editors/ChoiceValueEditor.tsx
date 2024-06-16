import { useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
import { SearchOutlined } from '@ant-design/icons';
import type { ValueEditorProps } from '../models';
import { Button, Input, Modal } from '@/components/bakaui';
import type { DestroyableProps } from '@/components/bakaui/types';

type Data = { value: string; label: string };

type ChoiceValueEditorProps = ValueEditorProps<string[]> & DestroyableProps & {
  multiple: boolean;
  getDataSource: () => Promise<Data[] | undefined>;
};

export default ({ multiple, getDataSource, initValue, onChange }: ChoiceValueEditorProps) => {
  const { t } = useTranslation();

  const [dataSource, setDataSource] = useState<Data[]>([]);
  const [keyword, setKeyword] = useState('');
  const [value, setValue] = useState<string[]>(initValue ?? []);

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
        onChange?.(value);
      }}
    >
      <div>
        <Input
          size={'sm'}
          value={keyword}
          startContent={<SearchOutlined className={'text-small'} />}
          onValueChange={v => { setKeyword(v); }}
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
                  if (value.includes(d.value)) {
                    setValue(value.filter(v => v !== d.value));
                  } else {
                    setValue([...(d.value || []), d.value]);
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
