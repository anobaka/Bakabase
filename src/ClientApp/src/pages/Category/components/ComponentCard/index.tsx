import React, { useEffect, useState } from 'react';
import { Balloon } from '@alifd/next';
import './index.scss';
import i18n from 'i18next';

export default ({
  name,
  description,
  extra,
  selected: s,
  defaultSelected: ds,
  className,
  onSelect,
  disabled,
  tip,
  ...otherProps
}) => {
  const [selected, setSelected] = useState(ds);

  useEffect(() => {
    if (s !== undefined) {
      setSelected(s);
    }
  }, [s]);

  const coreComponent = (
    <div
      className={`${className} component-card ${selected ? 'selected' : ''} ${disabled ? 'disabled' : ''}`}
      {...otherProps}
      onClick={() => {
        if (disabled) {
          return;
        }
        if (s == undefined) {
          setSelected(!selected);
        }
        if (onSelect) {
          onSelect(!selected);
        }
      }}
    >
      <div className="select-cover" >
        {/* <Icon type="select" size={'large'} /> */}
      </div>
      <div className="name">{name}</div>
      {description && (
        <pre className="description">{i18n.t(description)}</pre>
      )}
      {extra}
    </div>
  );

  return (
    tip ? (
      <Balloon.Tooltip trigger={coreComponent}>{i18n.t(tip)}</Balloon.Tooltip>
    ) : coreComponent
  );
};
