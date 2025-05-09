import { BsFileEarmark, BsFolder } from 'react-icons/bs';
import { useTranslation } from 'react-i18next';
import { PathFilterFsType } from '@/pages/MediaLibraryTemplate/models';

export default ({ type }: {type: PathFilterFsType}) => {
  const { t } = useTranslation();
  switch (type) {
    case PathFilterFsType.File:
      return (
        <div className={'inline-flex items-center gap-1'}>
          <BsFileEarmark />
          {t(PathFilterFsType[type])}
        </div>
      );
    case PathFilterFsType.Directory:
      return (
        <div className={'inline-flex items-center gap-1'}>
          <BsFolder />
          {t(PathFilterFsType[type])}
        </div>
      );
  }
};
