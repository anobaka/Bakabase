import React from 'react';
import { useTranslation } from 'react-i18next';
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
import CustomIcon from '@/components/CustomIcon';

export default () => {
  const { t } = useTranslation();
  const appContext = store.useModelState('appContext');

  const apiDocumentUrl = appContext.apiEndpoint ? `${appContext.apiEndpoint}/swagger` : undefined;

  const items = [
    {
      label: 'Listening addresses',
      value: (
        <div className={'flex flex-wrap gap-1 items-center'}>
          {appContext.listeningAddresses?.map(x => (
            <Snippet
              symbol={<>&nbsp;</>}
              size={'sm'}
              variant={'flat'}
            >
              {x}
            </Snippet>
          ))}
        </div>
      ),
    },
    {
      label: 'API endpoint',
      value: (
        appContext.apiEndpoint && (
          <div className={'flex flex-wrap gap-1 items-center'}>
            <Snippet
              symbol={<>&nbsp;</>}
              size={'sm'}
              variant={'flat'}
            >
              {appContext.apiEndpoint}
            </Snippet>
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
                <div style={{ display: 'flex', alignItems: 'center' }}>
                  {t(c.label)}
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
