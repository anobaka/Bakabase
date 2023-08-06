import React from 'react';
import { Button } from '@alifd/next';
import './index.scss';
import i18n from 'i18next';
import { Link } from 'ice';

export default ({
  title,
  titleAfter,
  buttons = [],
  right,
}) => {
  return (
    <div className={'bakabase-title'}>
      <div className="left">
        <div className="title">{i18n.t(title)}</div>
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
                {b.type == 'link' ? (<Link to={b.to}>{i18n.t(b.text)}</Link>) :
                  (<Button type={'primary'} onClick={b.onClick} text>{i18n.t(b.text)}</Button>)
                }
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
