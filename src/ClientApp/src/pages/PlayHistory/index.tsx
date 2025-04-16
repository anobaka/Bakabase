import React, { useEffect, useReducer, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  DeleteOutlined, DownloadOutlined,
  EnterOutlined,
  MergeOutlined,
  PlusCircleOutlined,
  SearchOutlined,
  ToTopOutlined, UploadOutlined,
} from '@ant-design/icons';
import { AliasAdditionalItem } from '@/sdk/constants';
import BApi from '@/sdk/BApi';
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Chip,
  Divider,
  Input,
  Modal,
  Pagination,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Tooltip,
} from '@/components/bakaui';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import FileSystemSelectorDialog from '@/components/FileSystemSelector/Dialog';

type Form = {
  pageSize: 100;
  pageIndex: number;
  // resourceId?: number;
};

type PlayHistory = {
  // resourceId: number;
  id: number;
  item?: string;
  playedAt?: string;
};

export default () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [form, setForm] = useState<Form>({
    pageSize: 100,
    pageIndex: 0,
  });
  const [playHistories, setPlayHistories] = useState<PlayHistory[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [, forceUpdate] = useReducer((x) => x + 1, 0);

  useEffect(() => {
    search();
  }, []);

  const search = (pForm?: Partial<Form>) => {
    const nf = {
      ...form,
      ...pForm,
    };
    setForm(nf);
    BApi.playHistory.searchPlayHistories(nf)
      .then((a) => {
        setPlayHistories(a.data ?? []);
        setTotalCount(a.totalCount!);
      });
  };

  const renderPagination = () => {
    const pageCount = Math.ceil(totalCount / form.pageSize);
    if (pageCount > 1) {
      return (
        <div className={'flex justify-center'}>
          <Pagination
            size={'sm'}
            page={form.pageIndex}
            total={pageCount}
            onChange={(p) => search({ pageIndex: p })}
          />
        </div>
      );
    }
    return;
  };

  return (
    <div className="">
      {playHistories.length > 0 ? (
        <div className={'mt-1'}>
          <Table
            removeWrapper
            topContent={renderPagination()}
            bottomContent={renderPagination()}
            isStriped
            isCompact
            isHeaderSticky
          >
            <TableHeader>
              <TableColumn>{t('Item')}</TableColumn>
              <TableColumn>{t('Played at')}</TableColumn>
            </TableHeader>
            <TableBody>
              {playHistories.map(a => {
                return (
                  <TableRow key={a.id}>
                    <TableCell>{a.item}</TableCell>
                    <TableCell>
                      {a.playedAt}
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </div>
      ) : (
        <div>
          {t('No play history')}
        </div>
      )}
    </div>
  );
};
