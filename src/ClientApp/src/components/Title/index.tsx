import React from 'react';
import { Button } from '@alifd/next';
import './index.scss';
import i18n from 'i18next';
import { Link } from 'ice';
import { useTranslation } from 'react-i18next';

export default ({
  title,
  titleAfter = undefined,
  buttons = [],
  right = undefined,
}) => {
  const { t } = useTranslation();

  return (
    <div className={'iw-title'}>
      <div className="left">
        <div className="title">{t(title)}</div>
        {titleAfter && (
          <div className={'title-after'}>
            {titleAfter}
          </div>
        )}
        {buttons && (
          <div className="buttons">
            {buttons.map((b) => (
              <div className={'button'} key={b.key}>
                <img className={'icon'} src={b.icon} alt="" />
                {b.type == 'link' ? (<Link to={b.to}>{t(b.text)}</Link>)
                  : (<Button type={'primary'} onClick={b.onClick} text>{t(b.text)}</Button>)}
              </div>
            ))}
          </div>
        )}
      </div>
      <div className="right">
        {right}
      </div>
    </div>
  );
};
