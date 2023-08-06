import React from 'react';
import { Button, Input, Balloon } from '@alifd/next';
import i18n from 'i18next';

const emptyInputLabel = i18n.t('Double click here to set');

export default class EditablePane extends React.Component {
  constructor(props) {
    super(props);
    this.onKeyDown = (e) => {
      const { key, keyCode } = e;
      if (keyCode > 36 && keyCode < 41) {
        e.stopPropagation();
      }
      if (key == 'Enter') {
        this.finishEditing(e.target.value);
      }
    };
    this.onBlur = (e) => {
      this.finishEditing(e.target.value);
    };

    this.finishEditing = (v) => {
      this.setState({
        editable: false,
        value: v,
      });
      if (this.props.onEditingFinished) {
        this.props.onEditingFinished(v);
      }
    };

    this.onDblClick = () => {
      this.setState({
        editable: true,
      }, () => {
        if (this.props.onDoubleClick) {
          this.props.onDoubleClick();
        }
      });
    };
    this.state = {
      value: this.props.value ?? this.props.defaultValue,
      editable: false,
    };
  }

  UNSAFE_componentWillReceiveProps(nextProps) {
    if (nextProps.value !== this.state.value) {
      this.setState({
        value: nextProps.value,
      });
    }
    // console.log(nextProps, this.props);
  }

  // Stop bubble up the events of keyUp, keyDown, keyLeft, and keyRight
  render() {
    const {
      value,
      editable,
    } = this.state;
    if (editable) {
      console.log(value);
      return (
        <Input
          autoFocus
          onKeyDown={this.onKeyDown}
          onBlur={this.onBlur}
          onChange={(v) => {
            this.setState({
              value: v,
            });
            this.props.onChange && this.props.onChange(v);
          }}
          value={value}
          {...(this.props.inputProps ?? {})}
        />
      );
    }
    return value ? (
      <Balloon.Tooltip
        trigger={(
          <span title={this.props.title} onDoubleClick={this.onDblClick}>{value}</span>
      )}
        align={'t'}
      >
        {i18n.t('Double click to edit')}
      </Balloon.Tooltip>
    ) :
      <span title={this.props.title} onDoubleClick={this.onDblClick}>{<Button type={'primary'} text>{emptyInputLabel}</Button>}</span>;
  }
}
