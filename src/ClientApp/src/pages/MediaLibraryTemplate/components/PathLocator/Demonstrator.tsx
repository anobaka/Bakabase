import { useTranslation } from 'react-i18next';
import { IoLayersOutline } from 'react-icons/io5';
import { BsRegex } from 'react-icons/bs';
import type { PathFilter, PathLocator } from '@/pages/MediaLibraryTemplate/models';
import { pathFilterFsTypes, PathPositioner } from '@/sdk/constants';
import PathFilterFsTypeBlock from '@/pages/MediaLibraryTemplate/components/PathFilterFsTypeBlock';

type Props = {
  locator: PathLocator;
};

export default ({ locator }: Props) => {
  const { t } = useTranslation();

  const renderPositioner = () => {
    switch (locator.positioner) {
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
    switch (locator.positioner) {
      case PathPositioner.Layer:
        return locator.layer == undefined ? t('Not set') : locator.layer == 0 ? t('Current path') : t('The {{layer}} layer', { layer: locator.layer });
      case PathPositioner.Regex:
        return locator.regex ?? t('Not set');
      default:
        return t('Not supported');
    }
  };


  return (
    <div className={'flex items-center gap-1'}>
      <div className={'flex items-center gap-1'}>
        {t('Through')}
        {renderPositioner()}
      </div>
      <div className={'flex items-center gap-1'}>
        {renderLayerOrRegex()}
      </div>
    </div>
  );
};
