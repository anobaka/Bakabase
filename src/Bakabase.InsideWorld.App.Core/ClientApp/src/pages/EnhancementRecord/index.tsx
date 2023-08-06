import React, { useEffect, useState } from 'react';
import { Icon, Pagination, Select, Table } from '@alifd/next';
import './index.scss';
import i18n from 'i18next';
import { SearchEnhancementRecords } from '@/sdk/apis';
import BApi from '@/sdk/BApi';
import { ComponentType } from '@/sdk/constants';

const successDataSource = [
  { label: i18n.t('All'), value: '' },
  { label: i18n.t('Success'), value: true },
  { label: i18n.t('Fail'), value: false },
];

export default () => {
  const [records, setRecords] = useState([]);
  const [form, setForm] = useState({});
  const [enhancers, setEnhancers] = useState<{label: string; value: string}[]>([]);

  const search = (f = {}) => {
    setForm({ ...f });
    BApi.enhancementRecord.searchEnhancementRecords(f).then((t) => {
      setRecords(t.data);
      setForm({
        ...f,
        pageIndex: t.pageIndex,
        pageSize: t.pageSize,
        totalCount: t.totalCount,
      });
    });
  };

  useEffect(() => {
    BApi.component.getComponentDescriptors({ type: ComponentType.Enhancer }).then(a => {
      setEnhancers(a.data.map(b => ({
        label: b.name,
        value: b.id,
      })));
    });

    search({
      pageIndex: 1,
      pageSize: 20,
    });
  }, []);

  console.log(form);

  return (
    <div className={'enhancement-record-page'}>
      {i18n.t('This is a temporary page to diagnose the enhancers quickly.')}
      <div className="filter">
        <div className="item">
          <div className="label">{i18n.t('Enhancer')}</div>
          <div className="value">
            <Select
              autoWidth={false}
              size={'small'}
              dataSource={enhancers}
              value={form.enhancerDescriptorId}
              onChange={(v) => search({
                ...form,
                enhancerDescriptorId: v,
              })}
            />
          </div>
        </div><div className="item">
          <div className="label">{i18n.t('Status')}</div>
          <div className="value">
            <Select
              size={'small'}
              dataSource={successDataSource}
              value={form.success}
              onChange={(v) => search({
                ...form,
                success: v,
              })}
            />
          </div>
        </div>
        <Pagination
          size={'small'}
          current={form.pageIndex}
          pageSize={20}
          total={form.totalCount}
          onChange={(p) => {
            search({
              ...form,
              pageIndex: p,
            });
          }}
        />
      </div>
      <Table dataSource={records} size={'small'}>
        <Table.Column title={i18n.t('Enhance Time')} dataIndex={'createDt'} width={'10%'} />
        <Table.Column title={i18n.t('Resource')} dataIndex={'resourceFullname'} width={'10%'} />
        <Table.Column title={i18n.t('Enhancer')} dataIndex={'enhancerName'} width={'15%'} />
        <Table.Column title={i18n.t('Status')} dataIndex={'success'} width={'5%'} cell={(c) => <Icon style={{ color: c ? 'green' : 'red' }} type={c ? 'success' : 'error'} />} />
        <Table.Column title={i18n.t('Enhancements')} dataIndex={'enhancement'} cell={(c) => (<pre>{c}</pre>)} width={'50%'} />
        <Table.Column title={i18n.t('Message')} dataIndex={'message'} cell={(c) => (<pre>{c}</pre>)} width={'10%'} />
      </Table>
    </div>
  );
};
