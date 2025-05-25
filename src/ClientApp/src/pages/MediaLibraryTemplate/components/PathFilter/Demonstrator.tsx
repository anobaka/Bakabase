import { useTranslation } from 'react-i18next';
import { BsRegex } from 'react-icons/bs';
import { IoLayersOutline } from 'react-icons/io5';
import type { PathFilter } from '@/pages/MediaLibraryTemplate/models';
import { PathFilterFsType } from '@/pages/MediaLibraryTemplate/models';
import { pathFilterFsTypes, PathPositioner } from '@/sdk/constants';
import PathFilterFsTypeBlock from '@/pages/MediaLibraryTemplate/components/PathFilterFsTypeBlock';
import { Chip } from '@/components/bakaui';

type Props = {
  filter: PathFilter;
};

export default ({ filter }: Props) => {
  const { t } = useTranslation();

  const renderPositioner = () => {
    switch (filter.positioner) {
      case PathPositioner.Layer:
        return (
          <>
            <IoLayersOutline className={'text-medium'} />
            {t('Layer')}
          </>
        );
      case PathPositioner.Regex:
        return (
          <>
            <BsRegex className={'text-medium'} />
            {t('Regex')}
          </>
        );
      default:
        return t('Not supported');
    }
  };

  const renderLayerOrRegex = () => {
    switch (filter.positioner) {
      case PathPositioner.Layer:
        return filter.layer == undefined ? t('Not set') : filter.layer == 0 ? t('Directory of media library') : t('The {{layer}} layer', { layer: filter.layer });
      case PathPositioner.Regex:
        return filter.regex ?? t('Not set');
      default:
        return t('Not supported');
    }
  };

  const renderFsTypeBlocks = () => {
    if (filter.fsType) {
      return <PathFilterFsTypeBlock type={filter.fsType} />;
    } else {
      return pathFilterFsTypes.map(t => <PathFilterFsTypeBlock type={t.value} />);
    }
  };

  const renderExtensions = () => {
    const texts = (filter.extensionGroups?.map(x => x.name) ?? []).concat(filter.extensions ?? []);
    return (
      <div className={'flex items-center gap-1'}>
        {texts.map(t => {
          return (
            <Chip size={'sm'} radius={'sm'} variant={'flat'}>
              {t}
            </Chip>
          );
        })}
      </div>
    );
  };

  return (
    <div className={'flex items-center gap-2'}>
      <div className={'flex items-center gap-1'}>
        {t('Through')}
        {renderPositioner()}
      </div>
      <div className={'flex items-center gap-1'}>
        {renderLayerOrRegex()}
      </div>
      <div className={'flex items-center gap-1'}>
        {renderFsTypeBlocks()}
      </div>
      {filter.fsType == PathFilterFsType.File && renderExtensions()}
    </div>
  );
};
