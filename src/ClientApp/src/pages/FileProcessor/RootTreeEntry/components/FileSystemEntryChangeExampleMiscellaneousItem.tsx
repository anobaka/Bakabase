import React from 'react';
import { useTranslation } from 'react-i18next';
import FileSystemEntryChangeExampleItem from './FileSystemEntryChangeExampleItem';

type Props = {
  indent?: 0 | 1 | 2;
  parent?: string;
};

export default ({ indent, parent }: Props) => {
  const { t } = useTranslation();
  return (
    <FileSystemEntryChangeExampleItem
      layer={indent}
      text={parent ? `${t('Other files in {{parent}}', { parent })}...` : `${t('Other files')}...`}
      type={'others'}
      className={'opacity-60'}
    />
  );
};
