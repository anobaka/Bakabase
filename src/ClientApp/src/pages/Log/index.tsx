import React, { useEffect, useRef, useState } from 'react';
import './index.scss';
import { Button, DatePicker2, Icon, Input, Pagination, Select, Table } from '@alifd/next';
import IceLabel from '@icedesign/label';
import moment from 'moment';
import { useUpdateEffect } from 'react-use';
import i18n from 'i18next';
import { useTranslation } from 'react-i18next';
import BApi from '@/sdk/BApi';
import { LogLevel, logLevels } from '@/sdk/constants';

const testLogs = [
  {
    dateTime: '2021-02-02 12:12:12',
    logger: 'SyncService',
    level: LogLevel.Error,
    event: 'Complete',
    message: 'Message mafsasdassad Message mafsasdassad Message mafsasdassad Message mafsasdassad Message mafsasdassad Message mafsasdassad Message mafsasdassad Message mafsasdassad Message mafsasdassad Message mafsasdassad',
    read: false,
  },
  {
    dateTime: '2021-02-02 12:12:13',
    logger: 'SyncService',
    level: LogLevel.Error,
    event: 'Complete',
    message: 'Message mafsasdassad Message mafsasdassad Message mafsasdassad Message mafsasdassad Message mafsasdassad Message mafsasdassad Message mafsasdassad Message mafsasdassad Message mafsasdassad Message mafsasdassad',
    read: true,
  },
];

export default () => {
  const { t } = useTranslation();
  const [logs, setLogs] = useState<any[]>([]);
  const [form, setForm] = useState({
    pageIndex: 1,
    pageSize: 100,
  });

  const [pageable, setPageable] = useState({
    pageIndex: 1,
    totalCount: 0,
  });

  useUpdateEffect(() => {
    // console.log(form);
    search();
  }, [form]);

  useEffect(() => {
    search();
  }, []);

  const search = async () => {
    const d = await BApi.log.searchLogs(form);
    setLogs(d.data);
    setPageable({
      pageIndex: d.pageIndex,
      totalCount: d.totalCount,
    });
  };

  const renderPagination = () => {
    if (pageable.totalCount > 0) {
      return (
        <div className={'pagination'}>
          <Pagination
            total={pageable.totalCount}
            pageSize={form.pageSize}
            current={pageable.pageIndex}
            size={'small'}
            onChange={p => {
              const newPageable = {
                ...pageable,
                pageIndex: p,
              };
              setPageable(newPageable);
              setForm({
                ...form,
                ...newPageable,
              });
            }}
          />
        </div>
      );
    }
    return undefined;
  };

  const patchForm = (patches = {}) => {
    setPageable({
      ...pageable,
      pageIndex: 1,
    });
    setForm({
      ...form,
      ...patches,
      pageIndex: 1,
    });
  };

  return (
    <div className={'log-page'}>
      <div className="opt">
        <div className="left">
          <div className="item">
            <div className="label">{t('Time')}</div>
            <div className="value">
              <DatePicker2.RangePicker
                size={'small'}
                format="YYYY/MM/DD HH:mm:ss"
                showTime
                onChange={dayjsArr => {
                  if (dayjsArr) {
                    const d1 = dayjsArr[0];
                    if (d1) {
                      patchForm({ startDt: d1.format('YYYY MM DD HH:mm:ss') });
                    }
                    const d2 = dayjsArr[1];
                    if (d2) {
                      patchForm({ endDt: d2.format('YYYY MM DD HH:mm:ss') });
                    }
                  }
                }}
              />
            </div>
          </div>
          <div className="item">
            <div className="label">{t('Level')}</div>
            <div className="value">
              <Select
                dataSource={logLevels}
                size={'small'}
                onChange={level => {
                  patchForm({
                    level,
                  });
                }}
              />
            </div>
          </div>
          <div className="item">
            <div className="label">{t('Logger')}</div>
            <div className="value">
              <Input
                size={'small'}
                onChange={logger => {
                  patchForm({
                    logger,
                  });
                }}
              />
            </div>
          </div>
          <div className="item">
            <div className="label">{t('Event')}</div>
            <div className="value">
              <Input
                size={'small'}
                onChange={event => {
                  patchForm({
                    event,
                  });
                }}
              />
            </div>
          </div>
          <div className="item">
            <div className="label">{t('Message')}</div>
            <div className="value">
              <Input
                size={'small'}
                onChange={message => {
                  patchForm({
                    message,
                  });
                }}
              />
            </div>
          </div>

        </div>
        <div className="right">
          <Button
            type={'normal'}
            size={'small'}
            onClick={() => BApi.log.clearAllLog().then((a) => setLogs([]))}
          >
            {t('Clear all')}
          </Button>
        </div>
      </div>
      {renderPagination()}
      <Table
        className={'logs'}
        size={'small'}
        dataSource={logs}
      >
        <Table.Column
          align={'center'}
          // sortable
          width={'8%'}
          dataIndex={'dateTime'}
          title={t('Time')}
          cell={(d) => moment(d)
            .format('MM-DD HH:mm:ss')}
        />
        <Table.Column
          // sortable
          align={'center'}
          width={'5%'}
          dataIndex={'level'}
          title={t('Level')}
          cell={(l) => (<IceLabel
            inverse={false}
            status={(l == LogLevel.Error || l == LogLevel.Critical || l == LogLevel.Warning) ? 'danger' : 'info'}
          >{l == LogLevel.Information ? 'Info' : LogLevel[l]}
          </IceLabel>)}
        />
        <Table.Column
          // sortable
          align={'center'}
          width={'25%'}
          // alignHeader={'center'}
          dataIndex={'logger'}
          className={'logger'}
          title={t('Logger')}
        />
        <Table.Column
          // sortable
          align={'center'}
          width={'8%'}
          dataIndex={'event'}
          title={t('Event')}
        />
        <Table.Column
          // sortable
          width={'50%'}
          alignHeader={'center'}
          dataIndex={'message'}
          title={t('Message')}
          cell={(c) => <pre>{c}</pre>}
        />
        {/* <Table.Column */}
        {/*  // sortable */}
        {/*  filters={[{ label: 'Unread', value: false }, { label: 'Read', value: true }]} */}
        {/*  filterMode={'single'} */}
        {/*  width={'10%'} */}
        {/*  dataIndex={'read'} */}
        {/*  title={'Read'} */}
        {/*  cell={(c, i, r) => (!c && <Button type={'primary'} text onClick={() => ReadLog({ id: r.id }).invoke((a) => loadAllLogs())}>Read</Button>)} */}
        {/* /> */}
      </Table>
      {renderPagination()}
    </div>
  );
};
