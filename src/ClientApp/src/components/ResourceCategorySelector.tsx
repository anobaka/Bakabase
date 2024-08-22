import React, { useEffect, useState } from 'react';
import { Select } from '@alifd/next';
import { Link } from 'ice';
import i18n from 'i18next';
import type { SelectProps } from '@alifd/next/types/select';
import { useTranslation } from 'react-i18next';
import { GetAllResourceCategories } from '@/sdk/apis';
import BApi from '@/sdk/BApi';

type IProps = SelectProps;

export default (props: IProps) => {
  const { t } = useTranslation();
  const [categories, setCategories] = useState([]);

  useEffect(() => {
    BApi.category.getAllResourceCategories().then((a) => {
      // @ts-ignore
      setCategories(a.data.map((b) => ({ label: b.name, value: b.id })));
    });
  }, []);

  return (
    <Select
      dataSource={categories}
      placeholder={t('Choose a category')}
      notFoundContent={<Link to={'/bakabase/category'}>Categories are not set, click to set.</Link>}
      {...props}
    />
  );
};
