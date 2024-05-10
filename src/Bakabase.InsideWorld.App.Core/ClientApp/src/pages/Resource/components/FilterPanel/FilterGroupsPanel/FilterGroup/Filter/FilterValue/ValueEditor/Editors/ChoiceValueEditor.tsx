import { useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
import { SearchOutlined } from '@ant-design/icons';
import type { ValueEditorProps } from '../models';
import { Button, Input, Modal } from '@/components/bakaui';

type Data = { id: string; value: string };

interface ChoiceValueEditorProps extends ValueEditorProps<string[]>{
  multiple: boolean;
  getDataSource: () => Promise<Data[] | undefined>;
}

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
          keyword.length == 0 || d.value.toLowerCase().includes(keyword.toLowerCase())).map(({ id, value: v }) => {
          return (
            <Button
              size={'sm'}
              color={value.includes(id) ? 'primary' : 'default'}
              onClick={() => {
                if (multiple) {
                  if (value.includes(id)) {
                    setValue(value.filter(v => v !== id));
                  } else {
                    setValue([...(value || []), id]);
                  }
                } else {
                  setValue([id]);
                }
              }}
            >
              {v}
            </Button>
          );
        })}
      </div>
    </Modal>
  );
};
