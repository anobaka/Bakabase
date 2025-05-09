import { useTranslation } from 'react-i18next';
import type { PathFilter } from '@/pages/MediaLibraryTemplate/models';
import { pathFilterFsTypes, PathPositioner } from '@/sdk/constants';
import PathFilterFsTypeBlock from '@/pages/MediaLibraryTemplate/components/PathFilterFsTypeBlock';

type Props = {
  filter: PathFilter;
};

export default ({ filter }: Props) => {
  const { t } = useTranslation();

  const renderLayerOrRegex = () => {
    switch (filter.positioner) {
      case PathPositioner.Layer:
        if (filter.layer == 0) {
          return t('Current path');
        }
        return t('The {{layer}} layer', { layer: filter.layer });
      case PathPositioner.Regex:
        return filter.regex;
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
    return (
      <>
        {filter.extensionGroups?.map(g => g.name).join(',')}
        {Array.from(filter.extensions ?? []).join(',')}
      </>
    );
  };

  return (
    <>
      <div>
        {t('Through')}
        {PathPositioner[filter.positioner]}
      </div>
      <div>
        {renderLayerOrRegex()}
      </div>
      <div>
        {renderFsTypeBlocks()}
      </div>
      <div>
        {renderExtensions()}
      </div>
    </>
  );
};
