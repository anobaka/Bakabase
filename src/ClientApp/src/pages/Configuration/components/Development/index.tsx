import React from 'react';
import { useTranslation } from 'react-i18next';
import { QuestionCircleOutlined } from '@ant-design/icons';
import {
  Button,
  Snippet, Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Tooltip,
} from '@/components/bakaui';
import store from '@/store';
import ExternalLink from '@/components/ExternalLink';

export default () => {
  const { t } = useTranslation();
  const appContext = store.useModelState('appContext');

  const apiDocumentUrl = appContext.apiEndpoint ? `${appContext.apiEndpoint}/swagger` : undefined;

  const items = [
    {
      label: 'API endpoints',
      tip: (
        <div>
          {t('Listening addresses:')}
          {appContext.listeningAddresses.map(addr => {
            return (
              <div>{addr}</div>
            );
          })}
        </div>
      ),
      value: (
        appContext.apiEndpoints && (
          <div className={'flex flex-wrap gap-1 items-center'}>
            {appContext.apiEndpoints?.map(x => (
              <Snippet
                symbol={<>&nbsp;</>}
                size={'sm'}
                variant={'flat'}
              >
                {x}
              </Snippet>
            ))}
          </div>
        )
      ),
    },
    {
      label: 'API document',
      value: (
        apiDocumentUrl && <ExternalLink href={apiDocumentUrl}>{apiDocumentUrl}</ExternalLink>
      ),
    },
  ];

  return (
    <Table
      removeWrapper
      isCompact
    >
      <TableHeader>
        <TableColumn width={200}>{t('Development')}</TableColumn>
        <TableColumn>&nbsp;</TableColumn>
      </TableHeader>
      <TableBody>
        {items.map((c, i) => {
          return (
            <TableRow key={i} className={'hover:bg-[var(--bakaui-overlap-background)]'}>
              <TableCell>
                <div className={'flex items-center gap-1'}>
                  {t(c.label)}
                  {c.tip && (
                    <Tooltip content={c.tip}>
                      <QuestionCircleOutlined className={'text-base'} />
                    </Tooltip>
                  )}
                </div>
              </TableCell>
              <TableCell>
                {c.value}
              </TableCell>
            </TableRow>
          );
        })}

      </TableBody>
    </Table>
  );
};
