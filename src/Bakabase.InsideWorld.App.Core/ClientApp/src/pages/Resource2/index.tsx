import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Pagination } from '@alifd/next';
import styles from './index.module.scss';
import FilterPanel from './components/FilterPanel';
import type { ISearchForm } from '@/pages/Resource2/models';
import { convertGroupToDto } from '@/pages/Resource2/helpers';
import BApi from '@/sdk/BApi';

const PageSize = 100;

export default () => {
  const { t } = useTranslation();
  const [pageable, setPageable] = useState<{page: number; pageSize: number; totalCount: number}>();


  const search = async (form: ISearchForm) => {
    const dto = {
      ...form,
      group: convertGroupToDto(form.group),
      pageSize: PageSize,
    };

    const rsp = await BApi.resource.searchResourcesV2(dto);
  };

  return (
    <div className={styles.resourcePage}>
      <FilterPanel onSearch={search} />
      {!pageable && (
        <div className={styles.pagination}>
          <Pagination />
        </div>
      )}
    </div>
  );
};
