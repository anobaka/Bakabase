import _ from 'lodash';
import { DeleteOutlined, PlusCircleOutlined } from '@ant-design/icons';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Button, ColorPicker, Input, Tree } from '@/components/bakaui';
import { buildUntitledLabel, uuidv4 } from '@/components/utils';
import type { MultilevelPropertyOptions } from '@/components/Property/models';
import { buildColorValueString } from '@/components/bakaui/components/ColorPicker';
import colors from '@/components/bakaui/colors';

type Props = {
  options?: MultilevelPropertyOptions;
  onChange?: (value: MultilevelPropertyOptions) => void;
};

type MultilevelData = {
  value: string;
  label?: string;
  color?: string;
  children?: MultilevelData[];
};

type TreeData = {
  title?: any;
  key: string;
  children?: TreeData[];
  selectable?: boolean;
  checkable?: boolean;
  disableCheckbox?: boolean;
};

export default ({
                  options: propOptions,
                  onChange,
                }: Props) => {
  const { t } = useTranslation();

  const [options, setOptions] = useState(propOptions ?? { allowAddingNewDataDynamically: true });
  const [editingKey, setEditingKey] = useState<string>();
  const [expandKeys, setExpandKeys] = useState<React.Key[] | undefined>(options.data?.map(d => d.value));

  const patchOptions = (patches: Partial<MultilevelPropertyOptions>) => {
    const newOptions = {
      ...options,
      ...patches,
    };
    setOptions(newOptions);
    onChange?.(newOptions);
  };

  const buildTreeDataSource = (data: MultilevelData[]): TreeData[] => {
    const ret: TreeData[] = [];
    for (const md of data) {
      const td: TreeData = {
        selectable: false,
        checkable: false,
        disableCheckbox: true,
        title: (
          <div className={'flex items-center gap-1'}>
            <ColorPicker
              color={md.color ?? colors.color}
              onChange={c => {
                md.color = buildColorValueString(c);
                patchOptions({ ...options });
              }}
            />
            {editingKey == md.value ? (
              <Input
                size={'sm'}
                variant={'flat'}
                value={md.label}
                onValueChange={v => {
                  md.label = v;
                  patchOptions({ ...options });
                }}
                onBlur={() => {
                  setEditingKey(undefined);
                }}
              />
            ) : (
              <Button
                variant={'light'}
                size={'sm'}
                radius={'sm'}
                onPress={() => {
                  setEditingKey(md.value);
                }}
              >
                {md.label}
              </Button>
            )}

            <Button
              variant={'light'}
              isIconOnly
              size={'sm'}
              radius={'sm'}
              onPress={() => {
                md.children ??= [];
                md.children.push({
                  value: uuidv4(),
                  label: buildUntitledLabel(t('Untitled'), md.children.map(c => c.label)),
                });
                patchOptions({ ...options });
                const newExpandedKeys = expandKeys ?? [];
                if (!newExpandedKeys.includes(md.value)) {
                  newExpandedKeys.push(md.value);
                }
                setExpandKeys([...newExpandedKeys]);
              }}
            >
              <PlusCircleOutlined className={'text-small'} />
            </Button>
            <Button
              variant={'light'}
              isIconOnly
              size={'sm'}
              radius={'sm'}
              color={'danger'}
              onPress={() => {
                data.splice(data.indexOf(md), 1);
                patchOptions({ ...options });
              }}
            >
              <DeleteOutlined className={'text-small'} />
            </Button>
          </div>
        ),
        key: md.value,
        children: md.children ? buildTreeDataSource(md.children) : undefined,
      };
      ret.push(td);
    }

    // log(ret, multilevelTreeExpandKeys);
    return ret;
  };

  const data = options.data ? buildTreeDataSource(options.data) : [];

  console.log(options);

  return (
    <div>
      <div>
        <Button
          size={'sm'}
          variant={'light'}
          onPress={() => {
            options.data ??= [];
            patchOptions({
              data: options.data.concat([{
                value: uuidv4(),
                label: buildUntitledLabel(t('Untitled'), options.data.map(c => c.label)),
              }]),
            });
          }}
        >
          <PlusCircleOutlined className={'text-small'} />
          {t('Add root data')}
        </Button>
      </div>
      <Tree
        selectable={false}
        checkable={false}
        showLine
        expandedKeys={expandKeys}
        treeData={data}
        onExpand={(keys, {
          expanded,
        }) => {
          setExpandKeys(expanded ? _.union(expandKeys, keys) : _.intersection(expandKeys, keys));
        }}
      />
    </div>
  );
};
