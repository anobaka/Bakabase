import { BsFileEarmark, BsFolder } from 'react-icons/bs';
import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import { Divider, Input, Modal, Radio, RadioGroup, Select, Textarea } from '@/components/bakaui';
import type { PathFilter } from '@/pages/MediaLibraryTemplate/models';
import { PathFilterFsType } from '@/pages/MediaLibraryTemplate/models';
import { pathFilterFsTypes, PathPositioner, pathPositioners } from '@/sdk/constants';
import type { DestroyableProps } from '@/components/bakaui/types';
import type { IdName } from '@/components/types';

type Props = {
  filter?: PathFilter;
  onSubmit?: (filter: PathFilter) => void;
  fileExtensionGroups?: IdName[];
} & DestroyableProps;

export default ({
                  filter: propsFilter,
                  onSubmit,
                  onDestroyed,
                  fileExtensionGroups,
                }: Props) => {
  const { t } = useTranslation();

  const [filter, setFilter] = useState<Partial<PathFilter>>(propsFilter ?? { positioner: PathPositioner.Layer });

  const renderFsItem = (type: PathFilterFsType) => {
    switch (type) {
      case PathFilterFsType.File:
        return (
          <div className={'inline-flex items-center gap-1'}>
            <BsFileEarmark />
            {t('File')}
          </div>
        );
      case PathFilterFsType.Directory:
        return (
          <div className={'inline-flex items-center gap-1'}>
            <BsFolder />
            {t('Folder')}
          </div>
        );
    }
  };

  const renderPositioner = () => {
    switch (filter.positioner) {
      case PathPositioner.Layer:
        return (
          <Select
            isRequired
            label={t('Layer')}
            dataSource={[0, 1, 2, 3, 4, 5, 6, 7, 8, 9].map(l => ({
              label: l,
              value: l.toString(),
            }))}
            selectedKeys={filter.layer == undefined ? undefined : [filter.layer.toString()]}
            onSelectionChange={keys => {
              const layer = parseInt(Array.from(keys)[0] as string, 10);
              setFilter({
                ...filter,
                layer,
              });
            }}
            description={t('Layer 0 is current path')}
          />
        );
      case PathPositioner.Regex:
        return (
          <Input
            isRequired
            label={t('Regex')}
            placeholder={t('Regex to match sub path')}
            value={filter.regex}
            onValueChange={v => {
              setFilter({
                ...filter,
                regex: v,
              });
            }}
          />
        );
      default:
        return t('Not supported');
    }
  };

  const isValid = () => {
    switch (filter.positioner) {
      case PathPositioner.Layer:
        return filter.layer != undefined && filter.layer >= 0;
      case PathPositioner.Regex:
        return filter.regex != undefined && filter.regex.length > 0;
      default:
        return false;
    }
  };

  return (
    <Modal
      size={'lg'}
      defaultVisible
      onDestroyed={onDestroyed}
      footer={{
        actions: ['ok', 'cancel'],
        okProps: {
          isDisabled: !isValid(),
        },
      }}
      onOk={() => {
        onSubmit?.(filter);
      }}
    >
      <div className={'flex flex-col gap-2'}>
        <RadioGroup
          label={t('Positioning')}
          onValueChange={v => {
            const nv = parseInt(v, 10);
            if (filter.positioner != nv) {
              setFilter({
                positioner: nv,
              });
            }
          }}
          value={filter.positioner?.toString()}
          orientation="horizontal"
          isRequired
        >
          {pathPositioners.map(p => (
            <Radio value={p.value.toString()}>{p.label}</Radio>
          ))}
        </RadioGroup>
        {filter.positioner && (
          <>
            {renderPositioner()}
            <Divider className="my-4" />
            <Select
              label={t('Limit path type')}
              placeholder={t('No limited')}
              dataSource={pathFilterFsTypes.map(t => ({
                label: renderFsItem(t.value),
                value: t.value,
                textValue: t.label,
              }))}
              onSelectionChange={keys => {
                const fsType = Array.from(keys)[0] as PathFilterFsType;
                setFilter({
                  ...filter,
                  fsType,
                });
              }}
              value={filter.fsType}
              selectionMode={'single'}
              renderValue={items => {
                return items.map(t => renderFsItem((t.data as { value: any })!.value));
              }}
            />
            {(filter.fsType == undefined || filter.fsType == PathFilterFsType.File) && (
              <>
                <Select
                  selectionMode={'multiple'}
                  label={t('Limit file type groups')}
                  placeholder={t('Select from predefined file type groups')}
                  dataSource={fileExtensionGroups?.map(l => ({
                    label: l.name,
                    value: l.id,
                  }))}
                  value={(filter.extensionGroupIds ?? []).map(x => x.toString())}
                  onSelectionChange={keys => {
                    setFilter({
                      ...filter,
                      extensionGroupIds: Array.from(keys).map(k => parseInt(k as string, 10)),
                    });
                  }}
                />
                <Textarea
                  label={t('Limit file extensions')}
                  placeholder={t('custom extensions, split by space')}
                  isMultiline
                  description={t('File type groups and extensions will be merged during filtering')}
                  value={(filter.extensions ?? []).join(' ')}
                  onValueChange={text => {
                    setFilter({
                      ...filter,
                      extensions: text.split(' ').map(t => t.trim()).map(t => `.${t.replace(/^[\.]+/, '')}`),
                    });
                  }}
                />
              </>
            )}
          </>
        )}
        <div>{JSON.stringify(filter)}</div>
      </div>
    </Modal>
  );
};
