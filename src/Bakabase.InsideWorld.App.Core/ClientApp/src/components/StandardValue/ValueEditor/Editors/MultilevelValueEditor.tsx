import { useTranslation } from 'react-i18next';
import React, { useCallback, useEffect, useState } from 'react';
import { DoubleRightOutlined, SearchOutlined } from '@ant-design/icons';
import type { ValueEditorProps } from '../models';
import { Button, Input, Modal } from '@/components/bakaui';
import type { MultilevelData } from '@/components/StandardValue/models';
import { filterMultilevelData, findNodeChainInMultilevelData } from '@/components/StandardValue/helpers';
import type { DestroyableProps } from '@/components/bakaui/types';

type Selectable<V> = (data: MultilevelData<V>, depth: number, index: number) => boolean;

interface MultilevelValueEditorProps<V> extends ValueEditorProps<V[], string[][]>, DestroyableProps{
  getDataSource?: () => Promise<MultilevelData<V>[] | undefined>;
  selectable?: Selectable<V>;
  multiple?: boolean;
}

const buildDefaultSelectable: (<V>() => Selectable<V>) = () => {
  return (data, depth, index) => data.children == undefined || data.children.length == 0;
};

export default <V = string>({ getDataSource, selectable = buildDefaultSelectable<V>(), value: propsValue, onValueChange, onCancel, multiple }: MultilevelValueEditorProps<V>) => {
  const { t } = useTranslation();

  const [dataSource, setDataSource] = useState<MultilevelData<V>[]>([]);
  const [keyword, setKeyword] = useState('');
  const [value, setValue] = useState<V[]>(propsValue ?? []);

  useEffect(() => {
    loadData();
  }, []);

  useEffect(() => {
    setValue(propsValue ?? []);
  }, [propsValue]);

  const loadData = async () => {
    const data = await getDataSource?.() ?? [];
    if (data.length > 0) {
      setDataSource(data);
    }
  };

  const renderTreeNodes = useCallback((data: MultilevelData<V>[]) => {
    const leaves = data.filter(d => !d.children || d.children.length == 0);
    const branches = data.filter(d => d.children && d.children.length > 0);

    return (
      <div className={'flex flex-col gap-1 rounded p-2 grow'} style={{ background: 'var(--bakaui-overlap-background)' }}>
        {leaves.length > 0 && (
          <div className={'flex flex-wrap gap-1'}>
            {leaves.map(({ value: nodeValue, label }, idx) => (
              <Button
                size={'sm'}
                key={idx}
                color={value.includes(nodeValue) ? 'primary' : 'default'}
                onClick={() => {
                  if (multiple) {
                    if (value.includes(nodeValue)) {
                      setValue(value.filter(v => v !== nodeValue));
                    } else {
                      setValue([...value, nodeValue]);
                    }
                  } else {
                    setValue([nodeValue]);
                  }
                }}
              >{label}</Button>
            ))}
          </div>
        )}
        {branches.length > 0 && (
          <div
            className={'grid items-center gap-1 grow'}
            style={{ gridTemplateColumns: 'auto auto minmax(0, 1fr)' }}
          >
            {branches.map(({ value, label, children }, idx) => {
              return (
                <React.Fragment key={idx}>
                  <div className={'flex justify-end'}>{label}</div>
                  <DoubleRightOutlined className={'text-small'} />
                  {renderTreeNodes(children!)}
                </React.Fragment>
              );
            })}
          </div>
        )}
      </div>
    );
  }, [value]);

  return (
    <Modal
      defaultVisible
      size={dataSource.length > 10 ? 'xl' : 'lg'}
      title={t('Select data')}
      onOk={async () => {
        onValueChange?.(value, value.map(v => findNodeChainInMultilevelData(dataSource, v)?.map(x => x.label)).filter(x => x) as string[][]);
      }}
      onClose={onCancel}
    >
      <div className={'flex flex-col gap-1 max-h-full min-h-0'}>
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
        <div className={'flex flex-wrap gap-1 w-full min-h-0 overflow-y-auto'}>
          {renderTreeNodes(filterMultilevelData(dataSource, keyword))}
        </div>
      </div>
    </Modal>
  );
};
