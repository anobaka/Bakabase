import { BsFileEarmark, BsFolder, BsLayers } from 'react-icons/bs';
import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import { SelectItem } from '@heroui/react';
import type { PathFilter } from '@/pages/MediaLibraryTemplate/models';
import { PathFilterFsType } from '@/pages/MediaLibraryTemplate/models';
import { Select } from '@/components/bakaui';
import { pathFilterFsTypes, PathPositioner, pathPositioners } from '@/sdk/constants';

type Props = {
  filter?: PathFilter;
};

export default ({ filter: propsFilter }: Props) => {
  const { t } = useTranslation();

  const [filter, setFilter] = useState(propsFilter);

  const renderLayer = () => {
    return (
      <div className={'inline-flex items-center gap-1'}>
        <BsLayers />
        The
        <Select dataSource={[0, 1, 2, 3, 4, 5, 6, 7, 8, 9].map(l => ({
          label: l,
          value: l,
        }))}
        />
        Layer
      </div>
    );
  };

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


  const renderFsType = () => {
    return (
      <Select
        dataSource={pathFilterFsTypes.map(t => ({
          label: renderFsItem(t.value),
          value: t.value,
          textValue: t.label,
        }))}
        isMultiline
        size={'sm'}
        renderValue={items => {
          return items.map(t => renderFsItem((t.data as {value: any})!.value));
        }}
      />
    );
  };

  const renderExtensions = () => {
    return (

    )
  }

  const renderExtensionGroups = () => {
    return (

    )
  }

  const renderPositioner = () => {
    switch (filter.positioner) {
      case PathPositioner.Layer:
        return (
          <>
            {renderLayer()}
            {renderFsType()}
            {filter.extensions ? ` with extensions ${Array.from(filter.extensions).join(',')}` : ''}
            in the current directory.
          </>
        );
      case PathPositioner.Regex:
        return (
          <>
            Matched path by {filter.regex}
          </>
        );
      default:
        return t('Not supported');
    }
  };

  return (
    <div className={'flex items-center gap-1'}>
      <Select label={t('Positioner')} dataSource={pathPositioners} />

    </div>
  );
};
