import i18n from 'i18next';
import React from 'react';
import { GithubOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import Urls from '@/cons/Urls';
import qqGroupImg from '@/assets/qq-group.png';
import { Button, Table, TableRow, TableBody, TableCell, TableColumn, TableHeader } from '@/components/bakaui';
import BApi from '@/sdk/BApi';

const contacts = [
  {
    label: 'Github',
    value: (
      // <ExternalLink to={Urls.Github}>
      <Button
        size={'sm'}
        color={'default'}
        onClick={() => {
          BApi.gui.openUrlInDefaultBrowser({ url: Urls.Github });
        }}
      >
        <GithubOutlined className={'text-lg'} />
        <span className={'font-bold'}>Github</span>
      </Button>
      // </ExternalLink>
    ),
  },
  {
    label: 'QQ',
    value: <img src={qqGroupImg} />,
  },
  // {
  //   label: 'WeChat',
  //   value: <ExternalLink to={Urls.WeChatQrCode}>Github</ExternalLink>,
  // },
];

export default () => {
  const { t } = useTranslation();
  return (
    <div className="group">
      <div className="settings">
        <Table
          removeWrapper
          isCompact
        >
          <TableHeader>
            <TableColumn width={200}>{t('Contact us')}</TableColumn>
            <TableColumn>&nbsp;</TableColumn>
          </TableHeader>
          <TableBody>
            {contacts.map((c, i) => {
              return (
                <TableRow key={i} className={'hover:bg-[var(--bakaui-overlap-background)]'}>
                  <TableCell>
                    {t(c.label)}
                  </TableCell>
                  <TableCell>
                    {c.value}
                  </TableCell>
                </TableRow>
              );
            })}

          </TableBody>
        </Table>
      </div>
    </div>
  );
};
