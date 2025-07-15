import { useTranslation } from 'react-i18next';
import { IoLayersOutline } from 'react-icons/io5';
import { BsRegex } from 'react-icons/bs';
import type { PathFilter, PathPropertyExtractor } from '@/pages/MediaLibraryTemplate/models';
import { pathFilterFsTypes, PathPositioner, PathPropertyExtractorBasePathType } from '@/sdk/constants';
import PathFilterFsTypeBlock from '@/pages/MediaLibraryTemplate/components/PathFilterFsTypeBlock';

type Props = {
  locator: PathPropertyExtractor;
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
        const basePathType = locator.basePathType ?? PathPropertyExtractorBasePathType.MediaLibrary;
        if (locator.layer == undefined) {
          return t('Not set');
        }
        if (locator.layer === 0) {
          return t('Self');
        }
        if (locator.layer > 0) {
          return t('The {{n}}th layer after {{basePathType}}', { basePathType: t(PathPropertyExtractorBasePathType[basePathType]), n: locator.layer });
        } else {
          return t('The {{n}}th layer before {{basePathType}}', { basePathType: t(PathPropertyExtractorBasePathType[basePathType]), n: Math.abs(locator.layer) });
        }
      case PathPositioner.Regex:
        return locator.regex ?? t('Not set');
      default:
        return t('Not supported');
    }
  };


  return (
    <div className={'flex items-center gap-1'}>
      <div className={'flex items-center gap-1'}>
        {renderPositioner()}
      </div>
      <div className={'flex items-center gap-1'}>
        {renderLayerOrRegex()}
      </div>
    </div>
  );
};
