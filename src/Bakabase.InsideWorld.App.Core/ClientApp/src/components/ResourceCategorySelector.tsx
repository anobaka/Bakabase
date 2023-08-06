import React, { useEffect, useState } from 'react';
import { Select } from '@alifd/next';
import { Link } from 'ice';
import { GetAllResourceCategories } from '@/sdk/apis';
import i18n from 'i18next';

export default (props = {}) => {
  const [categories, setCategories] = useState([]);

  useEffect(() => {
    GetAllResourceCategories().invoke((a) => {
      setCategories(a.data.map((b) => ({ label: b.name, value: b.id })));
    });
  }, []);

  return (
    <Select
      dataSource={categories}
      placeholder={i18n.t('Choose a category')}
      {...props}
      notFoundContent={<Link to={'/bakabase/category'}>Categories are not set, click to set.</Link>}
    />
  );
};
