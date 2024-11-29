'use strict';

import { useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
import { ArrowRightOutlined, SearchOutlined } from '@ant-design/icons';
import {
  Chip,
  Input,
  Kbd,
  Modal,
  Pagination,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
} from '@/components/bakaui';
import type { IProperty } from '@/components/Property/models';
import PropertyValueRenderer from '@/components/Property/components/PropertyValueRenderer';
import BApi from '@/sdk/BApi';
import type { DestroyableProps } from '@/components/bakaui/types';

type BulkModificationDiff = {
  resourcePath: string;
  diffs: Diff[];
};

type Diff = {
  property: IProperty;
  value1?: string;
  value2?: string;
};

const PageSize = 20;

type Props = {
  bmId: number;
} & DestroyableProps;

export default ({ bmId, onDestroyed }: Props) => {
  const { t } = useTranslation();

  const [keyword, setKeyword] = useState<string>();
  const [pageable, setPageable] = useState({
    page: 0,
    total: 0,
  });
  const [data, setData] = useState<BulkModificationDiff[]>();

  useEffect(() => {
    search(undefined, 1);
  }, []);

  const TPagination = () => {
    if (pageable.total > 1) {
      return (
        <div className={'flex justify-end'}>
          <Pagination
            total={pageable.total}
            page={pageable.page}
            onChange={page => {
              search(keyword, page);
            }}
          />
        </div>
      );
    }
    return null;
  };

  const search = (keyword: string | undefined, page: number) => {
    setPageable({
      ...pageable,
      page,
    });
    BApi.bulkModification.searchBulkModificationDiffs(bmId, {
      path: keyword,
      pageIndex: page,
      pageSize: PageSize,
    }).then(r => {
      setData(r.data || []);
      setPageable({
        ...pageable,
        total: r.totalCount / PageSize,
      });
    });
  };

  return (
    <Modal
      onDestroyed={onDestroyed}
      defaultVisible
      size={'xl'}
    >
      <div>
        <div className={'flex items-center'}>
          <Input
            startContent={<SearchOutlined className={'text-base'} />}
            placeholder={t('Search')}
            value={keyword}
            onValueChange={e => setKeyword(e)}
            onKeyDown={e => {
              if (e.key == 'Enter') {
                search(keyword, 1);
              }
            }}
            endContent={<Kbd keys={['enter']}>K</Kbd>}
          />
        </div>
      </div>
      <TPagination />
      {data ? data.length == 0 ? (
        <div className={'flex justify-center mt-4'}>
          <div>
            <div>{t('No data')}</div>
            <div>1. {t('Please ensure that the calculation operation has been executed.')}</div>
            <div>2. {t('If the calculation operation has been executed, there may be no resources that will be changed.')}</div>
            <div>3. {t('Please check the search criteria.')}</div>
          </div>
        </div>
      ) : (
        <Table>
          <TableHeader>
            <TableColumn>{t('Resource')}</TableColumn>
            <TableColumn>{t('Diffs')}</TableColumn>
          </TableHeader>
          <TableBody>
            {data.map(d => {
              return (
                <TableRow>
                  <TableCell>{d.resourcePath}</TableCell>
                  <TableCell>
                    {d.diffs.map(diff => {
                      return (
                        <div className={'flex items-center gap-2'}>
                          <div className={'flex items-center gap-1'}>
                            <Chip
                              size={'sm'}
                              color={'secondary'}
                              variant={'flat'}
                            >
                              {diff.property.poolName}
                            </Chip>
                            <Chip
                              size={'sm'}
                              color={'primary'}
                            >
                              {diff.property.name}
                            </Chip>
                          </div>
                          <div className={'flex items-center gap-1'}>
                            <PropertyValueRenderer
                              bizValue={diff.value1}
                              property={diff.property}
                            />
                            <ArrowRightOutlined className={'text-base'} />
                            <PropertyValueRenderer
                              bizValue={diff.value2}
                              property={diff.property}
                            />
                          </div>
                        </div>
                      );
                    })}
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      ) : (
        <div className={'flex justify-center mt-12'}>
          <Spinner />
        </div>
      )}
      <TPagination />
    </Modal>
  );
};
