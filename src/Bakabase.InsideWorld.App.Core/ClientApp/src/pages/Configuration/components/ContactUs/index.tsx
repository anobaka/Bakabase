import Title from '@/components/Title';
import i18n from 'i18next';
import { Table } from '@alifd/next';
import React from 'react';
import ExternalLink from '@/components/ExternalLink';
import Urls from '@/cons/Urls';
import qqGroupImg from '@/assets/qq-group.png';

const contacts = [
  {
    label: 'Github',
    value: (
      <ExternalLink to={Urls.Github}>
        <iframe
          src="https://ghbtns.com/github-btn.html?user=Bakabase&repo=InsideWorld&type=star&count=true&size=large"
          frameBorder="0"
          scrolling="0"
          width="170"
          height="30"
          title="GitHub"
        />
      </ExternalLink>
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
  return (
    <div className="group">
      <Title title={i18n.t('Contact us')} />
      <div className="settings">
        <Table
          dataSource={contacts}
          size={'small'}
          hasHeader={false}
          cellProps={(r, c) => {
            return {
              className: c == 0 ? 'key' : c == 1 ? 'value' : '',
            };
          }}
        >
          <Table.Column dataIndex={'label'} width={300} title={i18n.t('Contact us')} cell={(l) => i18n.t(l)} />
          <Table.Column dataIndex={'value'} />
        </Table>
      </div>
    </div>
  );
};
