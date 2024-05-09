import { useTranslation } from 'react-i18next';
import React, { useCallback, useEffect, useState } from 'react';
import { DoubleRightOutlined, SearchOutlined } from '@ant-design/icons';
import type { ValueEditorProps } from '../models';
import { Button, Input, Modal } from '@/components/bakaui';
import type { MultilevelData } from '@/components/StandardValue/models';
import { filterMultilevelData } from '@/components/StandardValue/helpers';

interface MultilevelValueEditorProps<V> extends ValueEditorProps<V[]>{
  getDataSource: () => Promise<MultilevelData<V>[] | undefined>;
}

export default <V = string>({ getDataSource, initValue, onChange }: MultilevelValueEditorProps<V>) => {
  const { t } = useTranslation();

  const [dataSource, setDataSource] = useState<MultilevelData<V>[]>([]);
  const [keyword, setKeyword] = useState('');

  useEffect(() => {
    loadData();
  }, []);

  const loadData = async () => {
    const data = await getDataSource() ?? [];
    if (data.length > 0) {
      setDataSource(data);
    }
  };

  const renderTreeNodes = useCallback((data: MultilevelData<V>[]) => {
    const leaves = data.filter(d => !d.children || d.children.length == 0);
    const branches = data.filter(d => d.children && d.children.length > 0);

    return (
      <div className={'flex flex-col gap-1 rounded p-2'} style={{ background: 'var(--bakaui-overlap-background)' }}>
        {leaves.length > 0 && (
          <div className={'flex flex-wrap gap-1'}>
            {leaves.map(({ value: nodeValue, label }, idx) => (
              <Button
                size={'sm'}
                key={idx}
                color={initValue?.includes(nodeValue) ? 'primary' : 'default'}
                onClick={() => {
                  if (initValue?.includes(nodeValue)) {
                    onChange?.(initValue.filter(v => v !== nodeValue));
                  } else {
                    onChange?.([...(initValue || []), nodeValue]);
                  }
                }}
              >{label}</Button>
            ))}
          </div>
        )}
        {branches.length > 0 && (
          <div
            className={'grid items-center gap-1'}
            style={{ gridTemplateColumns: 'auto auto 1fr' }}
          >
            {branches.map(({ value, label, children }, idx) => {
              return (
                <React.Fragment key={idx}>
                  <div>{label}</div>
                  <DoubleRightOutlined className={'text-small'} />
                  {renderTreeNodes(children!)}
                </React.Fragment>
              );
            })}
          </div>
        )}
      </div>
    );
  }, []);

  return (
    <Modal
      defaultVisible
      size={dataSource.length > 10 ? 'xl' : 'lg'}
      title={t('Select data')}
      onOk={async () => {}}
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
        {renderTreeNodes(filterMultilevelData(dataSource, keyword))}
      </div>
    </Modal>
  );
};
